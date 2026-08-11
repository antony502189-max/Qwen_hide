using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace QwenWorkOverlay;

public sealed record AudioEndpoint(string Id, string Name);

public sealed class AudioDeviceService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    public IReadOnlyList<AudioEndpoint> Inputs() => Endpoints(DataFlow.Capture);
    public IReadOnlyList<AudioEndpoint> Outputs() => Endpoints(DataFlow.Render);
    public string? DefaultInput() => GetDefault(DataFlow.Capture, Role.Multimedia);
    public string? DefaultCommunicationsInput() => GetDefault(DataFlow.Capture, Role.Communications);
    public string? DefaultOutput() => GetDefault(DataFlow.Render, Role.Multimedia);
    public string? DefaultCommunicationsOutput() => GetDefault(DataFlow.Render, Role.Communications);

    public MMDevice? Find(string? id, DataFlow flow, bool useDefault = true)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id))
                return useDefault ? _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia) : null;
            return _enumerator.GetDevice(id);
        }
        catch
        {
            return null;
        }
    }

    public bool ValidateVirtualMixOutput(string? candidateId, string? loopbackId, out string reason)
    {
        if (string.IsNullOrWhiteSpace(candidateId))
        {
            reason = "No virtual mix output selected";
            return false;
        }
        if (string.Equals(candidateId, loopbackId, StringComparison.OrdinalIgnoreCase))
        {
            reason = "Virtual output cannot be the loopback source";
            return false;
        }
        if (string.Equals(candidateId, DefaultOutput(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidateId, DefaultCommunicationsOutput(), StringComparison.OrdinalIgnoreCase))
        {
            reason = "Refusing to render the mix to a Windows default output";
            return false;
        }

        var device = Find(candidateId, DataFlow.Render, false);
        if (device is null)
        {
            reason = "Selected virtual output is unavailable";
            return false;
        }

        // Deliberately refuse physical speakers/headsets. A false positive here could create acoustic feedback.
        if (!VirtualMixOutputPolicy.IsRecognizedVirtualName(device.FriendlyName))
        {
            reason = "Selected output is not recognizably virtual";
            return false;
        }

        reason = "Virtual output validated";
        return true;
    }

    private IReadOnlyList<AudioEndpoint> Endpoints(DataFlow flow) =>
        _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active)
            .Select(x => new AudioEndpoint(x.ID, x.FriendlyName))
            .ToList();

    private string? GetDefault(DataFlow flow, Role role)
    {
        try { return _enumerator.GetDefaultAudioEndpoint(flow, role).ID; }
        catch { return null; }
    }

    public void Dispose() => _enumerator.Dispose();
}

public static class AudioFormatConverter
{
    public const int TargetSampleRate = 48000;
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00AA00389B71");

    public static float Pcm16ToFloat(short value) => Math.Clamp(value / 32768f, -1f, 1f);
    public static short FloatToPcm16(float value) =>
        (short)Math.Clamp(MathF.Round(Math.Clamp(value, -1f, 1f) * 32767f), short.MinValue, short.MaxValue);

    public static float[] ToMonoFloat48k(byte[] buffer, int byteCount, WaveFormat format)
    {
        if (byteCount <= 0 || format.Channels <= 0 || format.SampleRate <= 0 || format.BlockAlign <= 0) return [];
        var sourceFrames = byteCount / format.BlockAlign;
        if (sourceFrames <= 0) return [];

        var source = new float[sourceFrames];
        var bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        for (var frame = 0; frame < sourceFrames; frame++)
        {
            float sum = 0;
            for (var channel = 0; channel < format.Channels; channel++)
            {
                var offset = frame * format.BlockAlign + channel * bytesPerSample;
                sum += ReadSample(buffer, offset, format);
            }
            source[frame] = Math.Clamp(sum / format.Channels, -1f, 1f);
        }

        if (format.SampleRate == TargetSampleRate || sourceFrames == 1) return source;

        var targetFrames = Math.Max(1, (int)Math.Round(sourceFrames * (double)TargetSampleRate / format.SampleRate));
        var output = new float[targetFrames];
        var sourcePerTarget = format.SampleRate / (double)TargetSampleRate;
        for (var i = 0; i < targetFrames; i++)
        {
            var position = Math.Min(sourceFrames - 1d, i * sourcePerTarget);
            var left = (int)Math.Floor(position);
            var right = Math.Min(sourceFrames - 1, left + 1);
            var fraction = (float)(position - left);
            output[i] = source[left] + (source[right] - source[left]) * fraction;
        }
        return output;
    }

    private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        if (offset < 0 || offset >= buffer.Length) return 0;
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                      (format is WaveFormatExtensible ext && ext.SubFormat == IeeeFloatSubFormat);

        if (format.BitsPerSample == 8 && offset < buffer.Length)
            return (buffer[offset] - 128) / 128f;

        if (format.BitsPerSample == 16 && offset + 1 < buffer.Length)
            return Pcm16ToFloat(BitConverter.ToInt16(buffer, offset));

        if (format.BitsPerSample == 24 && offset + 2 < buffer.Length)
        {
            var value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
            if ((value & 0x00800000) != 0) value |= unchecked((int)0xFF000000);
            return Math.Clamp(value / 8388608f, -1f, 1f);
        }

        if (format.BitsPerSample == 32 && offset + 3 < buffer.Length)
        {
            if (isFloat)
            {
                var sample = BitConverter.ToSingle(buffer, offset);
                return float.IsFinite(sample) ? Math.Clamp(sample, -1f, 1f) : 0f;
            }
            return Math.Clamp(BitConverter.ToInt32(buffer, offset) / 2147483648f, -1f, 1f);
        }

        return 0;
    }
}

public static class AudioMixer
{
    public static float[] Mix(ReadOnlySpan<float> microphone, ReadOnlySpan<float> system, float micGain, float systemGain)
    {
        var n = Math.Max(microphone.Length, system.Length);
        var output = new float[n];
        micGain = Math.Clamp(micGain, 0f, 4f);
        systemGain = Math.Clamp(systemGain, 0f, 4f);
        for (var i = 0; i < n; i++)
        {
            var value = (i < microphone.Length ? microphone[i] * micGain : 0) +
                        (i < system.Length ? system[i] * systemGain : 0);
            output[i] = MathF.Tanh(value);
        }
        return output;
    }
}

public sealed class MixedAudioSession : IDisposable
{
    private const int PumpFrames = 480;
    private const int MaxQueuedFrames = AudioFormatConverter.TargetSampleRate / 2;
    private const int TrimToFrames = AudioFormatConverter.TargetSampleRate / 4;

    private readonly AudioDeviceService _devices;
    private readonly AppLogger? _log;
    private readonly ConcurrentQueue<float> _microphoneFrames = new();
    private readonly ConcurrentQueue<float> _loopbackFrames = new();
    private readonly object _lifecycleGate = new();
    private WasapiCapture? _mic;
    private WasapiLoopbackCapture? _loopback;
    private WasapiOut? _virtualOutput;
    private BufferedWaveProvider? _mixedProvider;
    private System.Threading.Timer? _pump;
    private int _generation;
    private long _microphoneBytes;
    private long _loopbackBytes;
    private long _mixedFrames;

    public bool MicrophoneReady { get; private set; }
    public bool LoopbackReady { get; private set; }
    public bool VirtualOutputReady { get; private set; }
    public bool Running { get; private set; }
    public long MicrophoneBytes => Interlocked.Read(ref _microphoneBytes);
    public long LoopbackBytes => Interlocked.Read(ref _loopbackBytes);
    public long MixedFrames => Interlocked.Read(ref _mixedFrames);
    public string MicrophoneState { get; private set; } = "Idle";
    public string LoopbackState { get; private set; } = "Idle";
    public string VirtualOutputState { get; private set; } = "Not configured";
    public string InjectionState { get; private set; } = "Not configured";

    public MixedAudioSession(AudioDeviceService devices, AppLogger? log = null)
    {
        _devices = devices;
        _log = log;
    }

    public void Start(string? microphoneId, string? loopbackId, string? virtualMixOutputId, float micGain, float systemGain)
    {
        lock (_lifecycleGate)
        {
            if (Running) return;
            StopCore();
            var generation = ++_generation;
            _microphoneFrames.Clear();
            _loopbackFrames.Clear();
            Interlocked.Exchange(ref _microphoneBytes, 0);
            Interlocked.Exchange(ref _loopbackBytes, 0);
            Interlocked.Exchange(ref _mixedFrames, 0);
            InjectionState = "Initializing";

            StartMicrophone(microphoneId, generation);
            StartLoopback(loopbackId, generation);
            StartVirtualOutput(virtualMixOutputId, loopbackId, generation);

            if (VirtualOutputReady)
            {
                _pump = new System.Threading.Timer(_ => Pump(micGain, systemGain, generation), null, 0, 10);
                InjectionState = "READY: virtual cable receives the mixed stream; select its paired microphone in Qwen";
            }
            else if (string.IsNullOrWhiteSpace(InjectionState))
            {
                InjectionState = "Unavailable: virtual mix output is not configured";
            }

            Running = MicrophoneReady || LoopbackReady || VirtualOutputReady;
            _log?.Info($"Audio start: mic={MicrophoneState}; loopback={LoopbackState}; virtual={VirtualOutputState}; running={Running}");
        }
    }

    private void StartMicrophone(string? id, int generation)
    {
        try
        {
            var device = _devices.Find(id, DataFlow.Capture);
            if (device is null)
            {
                MicrophoneState = "Unavailable: capture endpoint not found";
                return;
            }

            var capture = new WasapiCapture(device);
            var format = capture.WaveFormat;
            capture.DataAvailable += (_, e) =>
            {
                if (generation != Volatile.Read(ref _generation)) return;
                Interlocked.Add(ref _microphoneBytes, e.BytesRecorded);
                Enqueue(_microphoneFrames, AudioFormatConverter.ToMonoFloat48k(e.Buffer, e.BytesRecorded, format));
            };
            capture.RecordingStopped += (_, e) =>
            {
                if (generation != Volatile.Read(ref _generation)) return;
                if (e.Exception is not null) MicrophoneState = "Stopped: " + e.Exception.GetType().Name;
                MicrophoneReady = false;
            };
            _mic = capture;
            capture.StartRecording();
            MicrophoneReady = true;
            MicrophoneState = "READY: " + device.FriendlyName;
        }
        catch (Exception ex)
        {
            MicrophoneReady = false;
            MicrophoneState = "Unavailable: " + ex.GetType().Name;
            _log?.Error("Microphone shared capture failed: " + ex.GetType().Name);
        }
    }

    private void StartLoopback(string? id, int generation)
    {
        try
        {
            var device = _devices.Find(id, DataFlow.Render);
            if (device is null)
            {
                LoopbackState = "Unavailable: render endpoint not found";
                return;
            }

            var capture = new WasapiLoopbackCapture(device);
            var format = capture.WaveFormat;
            capture.DataAvailable += (_, e) =>
            {
                if (generation != Volatile.Read(ref _generation)) return;
                Interlocked.Add(ref _loopbackBytes, e.BytesRecorded);
                Enqueue(_loopbackFrames, AudioFormatConverter.ToMonoFloat48k(e.Buffer, e.BytesRecorded, format));
            };
            capture.RecordingStopped += (_, e) =>
            {
                if (generation != Volatile.Read(ref _generation)) return;
                if (e.Exception is not null) LoopbackState = "Stopped: " + e.Exception.GetType().Name;
                LoopbackReady = false;
            };
            _loopback = capture;
            capture.StartRecording();
            LoopbackReady = true;
            LoopbackState = "READY: " + device.FriendlyName;
        }
        catch (Exception ex)
        {
            LoopbackReady = false;
            LoopbackState = "Unavailable: " + ex.GetType().Name;
            _log?.Error("WASAPI loopback capture failed: " + ex.GetType().Name);
        }
    }

    private void StartVirtualOutput(string? virtualId, string? loopbackId, int generation)
    {
        if (!_devices.ValidateVirtualMixOutput(virtualId, loopbackId, out var reason))
        {
            VirtualOutputState = "Unavailable: " + reason;
            InjectionState = VirtualOutputState;
            return;
        }

        try
        {
            var device = _devices.Find(virtualId, DataFlow.Render, false);
            if (device is null)
            {
                VirtualOutputState = "Unavailable: virtual endpoint vanished";
                InjectionState = VirtualOutputState;
                return;
            }

            var provider = new BufferedWaveProvider(
                WaveFormat.CreateIeeeFloatWaveFormat(AudioFormatConverter.TargetSampleRate, 1))
            {
                BufferDuration = TimeSpan.FromMilliseconds(750),
                DiscardOnBufferOverflow = true
            };
            var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 40);
            output.PlaybackStopped += (_, e) =>
            {
                if (generation != Volatile.Read(ref _generation)) return;
                if (e.Exception is not null) VirtualOutputState = "Stopped: " + e.Exception.GetType().Name;
                VirtualOutputReady = false;
            };
            output.Init(provider);
            _mixedProvider = provider;
            _virtualOutput = output;
            output.Play();
            VirtualOutputReady = true;
            VirtualOutputState = "READY: " + device.FriendlyName;
        }
        catch (Exception ex)
        {
            VirtualOutputState = "Unavailable: " + ex.GetType().Name;
            InjectionState = "Unavailable: virtual output failed (" + ex.GetType().Name + ")";
            VirtualOutputReady = false;
            _log?.Error("Virtual mix output failed: " + ex.GetType().Name);
        }
    }

    private void Pump(float micGain, float systemGain, int generation)
    {
        if (generation != Volatile.Read(ref _generation)) return;
        var provider = _mixedProvider;
        if (provider is null) return;

        TrimLatency(_microphoneFrames);
        TrimLatency(_loopbackFrames);
        var microphone = Dequeue(PumpFrames, _microphoneFrames);
        var loopback = Dequeue(PumpFrames, _loopbackFrames);
        var mixed = AudioMixer.Mix(microphone, loopback, micGain, systemGain);
        var bytes = new byte[mixed.Length * sizeof(float)];
        Buffer.BlockCopy(mixed, 0, bytes, 0, bytes.Length);
        try
        {
            provider.AddSamples(bytes, 0, bytes.Length);
            Interlocked.Add(ref _mixedFrames, mixed.Length);
        }
        catch (Exception ex)
        {
            if (generation == Volatile.Read(ref _generation))
                _log?.Error("Mixed audio provider failed: " + ex.GetType().Name);
        }
    }

    public void Stop()
    {
        lock (_lifecycleGate)
            StopCore();
    }

    private void StopCore()
    {
        ++_generation;
        var pump = _pump;
        var mic = _mic;
        var loopback = _loopback;
        var virtualOutput = _virtualOutput;
        _pump = null;
        _mic = null;
        _loopback = null;
        _virtualOutput = null;
        _mixedProvider = null;

        try { pump?.Dispose(); } catch { }
        try { mic?.StopRecording(); mic?.Dispose(); } catch { }
        try { loopback?.StopRecording(); loopback?.Dispose(); } catch { }
        try { virtualOutput?.Stop(); virtualOutput?.Dispose(); } catch { }

        Running = false;
        MicrophoneReady = false;
        LoopbackReady = false;
        VirtualOutputReady = false;
        if (MicrophoneState.StartsWith("READY", StringComparison.Ordinal)) MicrophoneState = "Idle";
        if (LoopbackState.StartsWith("READY", StringComparison.Ordinal)) LoopbackState = "Idle";
        if (VirtualOutputState.StartsWith("READY", StringComparison.Ordinal)) VirtualOutputState = "Idle";
    }

    private static void Enqueue(ConcurrentQueue<float> queue, float[] samples)
    {
        foreach (var sample in samples) queue.Enqueue(sample);
        if (queue.Count > MaxQueuedFrames)
            while (queue.Count > TrimToFrames && queue.TryDequeue(out _)) { }
    }

    private static void TrimLatency(ConcurrentQueue<float> queue)
    {
        if (queue.Count <= MaxQueuedFrames) return;
        while (queue.Count > TrimToFrames && queue.TryDequeue(out _)) { }
    }

    private static float[] Dequeue(int count, ConcurrentQueue<float> queue)
    {
        var result = new float[count];
        for (var i = 0; i < count && queue.TryDequeue(out var sample); i++) result[i] = sample;
        return result;
    }

    public void Dispose() => Stop();
}

public sealed class AudioDefaultDeviceGuard
{
    public string? InputBefore { get; }
    public string? CommunicationsBefore { get; }
    public string? OutputBefore { get; }
    public string? CommunicationsOutputBefore { get; }

    public AudioDefaultDeviceGuard(AudioDeviceService service)
    {
        InputBefore = service.DefaultInput();
        CommunicationsBefore = service.DefaultCommunicationsInput();
        OutputBefore = service.DefaultOutput();
        CommunicationsOutputBefore = service.DefaultCommunicationsOutput();
    }

    public bool Verify(AudioDeviceService service) =>
        InputBefore == service.DefaultInput() &&
        CommunicationsBefore == service.DefaultCommunicationsInput() &&
        OutputBefore == service.DefaultOutput() &&
        CommunicationsOutputBefore == service.DefaultCommunicationsOutput();
}

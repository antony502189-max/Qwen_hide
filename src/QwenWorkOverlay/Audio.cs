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
            if (string.IsNullOrWhiteSpace(id)) return useDefault ? _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia) : null;
            return _enumerator.GetDevice(id);
        }
        catch { return null; }
    }

    public bool ValidateVirtualMixOutput(string? candidateId, string? loopbackId, out string reason)
    {
        if (string.IsNullOrWhiteSpace(candidateId)) { reason = "No virtual mix output selected"; return false; }
        if (string.Equals(candidateId, loopbackId, StringComparison.OrdinalIgnoreCase)) { reason = "Virtual output cannot be the loopback source"; return false; }
        if (string.Equals(candidateId, DefaultOutput(), StringComparison.OrdinalIgnoreCase) || string.Equals(candidateId, DefaultCommunicationsOutput(), StringComparison.OrdinalIgnoreCase))
        {
            reason = "Refusing to render the mix to a Windows default output"; return false;
        }
        var device = Find(candidateId, DataFlow.Render, false);
        if (device is null) { reason = "Selected virtual output is unavailable"; return false; }
        // This deliberately refuses physical speakers/headsets. It supports common virtual-cable products without installing or configuring them.
        if (!VirtualMixOutputPolicy.IsRecognizedVirtualName(device.FriendlyName))
        {
            reason = "Selected output is not recognizably virtual"; return false;
        }
        reason = "Virtual output validated"; return true;
    }

    private IReadOnlyList<AudioEndpoint> Endpoints(DataFlow flow) => _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active).Select(x => new AudioEndpoint(x.ID, x.FriendlyName)).ToList();
    private string? GetDefault(DataFlow flow, Role role) { try { return _enumerator.GetDefaultAudioEndpoint(flow, role).ID; } catch { return null; } }
    public void Dispose() => _enumerator.Dispose();
}

public static class AudioFormatConverter
{
    public const int TargetSampleRate = 48000;
    public static float Pcm16ToFloat(short value) => Math.Clamp(value / 32768f, -1f, 1f);
    public static short FloatToPcm16(float value) => (short)Math.Clamp(MathF.Round(Math.Clamp(value, -1f, 1f) * 32767f), short.MinValue, short.MaxValue);

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
            source[frame] = sum / format.Channels;
        }
        if (format.SampleRate == TargetSampleRate) return source;
        var targetFrames = Math.Max(1, (int)Math.Round(sourceFrames * (double)TargetSampleRate / format.SampleRate));
        var output = new float[targetFrames];
        for (var i = 0; i < targetFrames; i++)
        {
            var sourceIndex = Math.Min(sourceFrames - 1, (int)Math.Floor(i * (double)format.SampleRate / TargetSampleRate));
            output[i] = source[sourceIndex];
        }
        return output;
    }

    private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        if (offset < 0 || offset >= buffer.Length) return 0;
        if (format.BitsPerSample == 16 && offset + 1 < buffer.Length) return Pcm16ToFloat(BitConverter.ToInt16(buffer, offset));
        if (format.BitsPerSample == 32 && offset + 3 < buffer.Length)
        {
            var candidate = BitConverter.ToSingle(buffer, offset);
            // WASAPI mix formats are normally IEEE float. If this is packed PCM32, scale its signed integer representation instead.
            return float.IsFinite(candidate) && Math.Abs(candidate) <= 8 ? candidate : BitConverter.ToInt32(buffer, offset) / 2147483648f;
        }
        return 0;
    }
}

public static class AudioMixer
{
    public static float[] Mix(ReadOnlySpan<float> microphone, ReadOnlySpan<float> system, float micGain, float systemGain)
    {
        var n = Math.Max(microphone.Length, system.Length); var output = new float[n];
        for (var i = 0; i < n; i++)
        {
            var value = (i < microphone.Length ? microphone[i] * micGain : 0) + (i < system.Length ? system[i] * systemGain : 0);
            output[i] = MathF.Tanh(value); // smooth limiter: no integer/float clipping reaches the virtual cable
        }
        return output;
    }
}

public sealed class MixedAudioSession : IDisposable
{
    private const int PumpFrames = 480; // 10 ms at 48 kHz
    private readonly AudioDeviceService _devices;
    private readonly ConcurrentQueue<float> _microphoneFrames = new();
    private readonly ConcurrentQueue<float> _loopbackFrames = new();
    private WasapiCapture? _mic;
    private WasapiLoopbackCapture? _loopback;
    private WasapiOut? _virtualOutput;
    private BufferedWaveProvider? _mixedProvider;
    private System.Threading.Timer? _pump;

    public bool MicrophoneReady { get; private set; }
    public bool LoopbackReady { get; private set; }
    public bool VirtualOutputReady { get; private set; }
    public bool Running { get; private set; }
    public long MicrophoneBytes { get; private set; }
    public long LoopbackBytes { get; private set; }
    public long MixedFrames { get; private set; }
    public string InjectionState { get; private set; } = "Not configured";

    public MixedAudioSession(AudioDeviceService devices) => _devices = devices;

    public void Start(string? microphoneId, string? loopbackId, string? virtualMixOutputId, float micGain, float systemGain)
    {
        if (Running) return;
        Stop();
        _microphoneFrames.Clear(); _loopbackFrames.Clear();
        StartMicrophone(microphoneId);
        StartLoopback(loopbackId);
        StartVirtualOutput(virtualMixOutputId, loopbackId);
        if (VirtualOutputReady)
        {
            _pump = new System.Threading.Timer(_ => Pump(micGain, systemGain), null, 0, 10);
            InjectionState = "READY: virtual cable receives the mixed stream; select its paired microphone in Qwen";
        }
        else if (string.IsNullOrWhiteSpace(InjectionState)) InjectionState = "Unavailable: virtual mix output is not configured";
        Running = MicrophoneReady || LoopbackReady;
    }

    private void StartMicrophone(string? id)
    {
        try
        {
            var device = _devices.Find(id, DataFlow.Capture);
            if (device is null) return;
            _mic = new WasapiCapture(device);
            _mic.DataAvailable += (_, e) => { MicrophoneBytes += e.BytesRecorded; Enqueue(_microphoneFrames, AudioFormatConverter.ToMonoFloat48k(e.Buffer, e.BytesRecorded, _mic.WaveFormat)); };
            _mic.StartRecording(); MicrophoneReady = true;
        }
        catch { MicrophoneReady = false; }
    }

    private void StartLoopback(string? id)
    {
        try
        {
            var device = _devices.Find(id, DataFlow.Render);
            if (device is null) return;
            _loopback = new WasapiLoopbackCapture(device);
            _loopback.DataAvailable += (_, e) => { LoopbackBytes += e.BytesRecorded; Enqueue(_loopbackFrames, AudioFormatConverter.ToMonoFloat48k(e.Buffer, e.BytesRecorded, _loopback.WaveFormat)); };
            _loopback.StartRecording(); LoopbackReady = true;
        }
        catch { LoopbackReady = false; }
    }

    private void StartVirtualOutput(string? virtualId, string? loopbackId)
    {
        if (!_devices.ValidateVirtualMixOutput(virtualId, loopbackId, out var reason)) { InjectionState = "Unavailable: " + reason; return; }
        try
        {
            var device = _devices.Find(virtualId, DataFlow.Render, false);
            if (device is null) { InjectionState = "Unavailable: virtual endpoint vanished"; return; }
            _mixedProvider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(AudioFormatConverter.TargetSampleRate, 1)) { BufferDuration = TimeSpan.FromSeconds(2), DiscardOnBufferOverflow = true };
            _virtualOutput = new WasapiOut(device, AudioClientShareMode.Shared, true, 50);
            _virtualOutput.Init(_mixedProvider); _virtualOutput.Play(); VirtualOutputReady = true;
        }
        catch (Exception ex) { InjectionState = "Unavailable: virtual output failed (" + ex.GetType().Name + ")"; VirtualOutputReady = false; }
    }

    private void Pump(float micGain, float systemGain)
    {
        if (_mixedProvider is null) return;
        var microphone = Dequeue(PumpFrames, _microphoneFrames);
        var loopback = Dequeue(PumpFrames, _loopbackFrames);
        var mixed = AudioMixer.Mix(microphone, loopback, micGain, systemGain);
        var bytes = new byte[mixed.Length * sizeof(float)]; Buffer.BlockCopy(mixed, 0, bytes, 0, bytes.Length);
        try { _mixedProvider.AddSamples(bytes, 0, bytes.Length); MixedFrames += mixed.Length; } catch { }
    }

    public void Stop()
    {
        _pump?.Dispose(); _pump = null;
        try { _mic?.StopRecording(); _mic?.Dispose(); } catch { }
        try { _loopback?.StopRecording(); _loopback?.Dispose(); } catch { }
        try { _virtualOutput?.Stop(); _virtualOutput?.Dispose(); } catch { }
        _mic = null; _loopback = null; _virtualOutput = null; _mixedProvider = null;
        Running = false; MicrophoneReady = false; LoopbackReady = false; VirtualOutputReady = false;
    }

    private static void Enqueue(ConcurrentQueue<float> queue, float[] samples)
    {
        foreach (var sample in samples) queue.Enqueue(sample);
        while (queue.Count > AudioFormatConverter.TargetSampleRate * 2) queue.TryDequeue(out _); // bound jitter memory at 2 seconds
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
        InputBefore = service.DefaultInput(); CommunicationsBefore = service.DefaultCommunicationsInput();
        OutputBefore = service.DefaultOutput(); CommunicationsOutputBefore = service.DefaultCommunicationsOutput();
    }
    public bool Verify(AudioDeviceService service) => InputBefore == service.DefaultInput() && CommunicationsBefore == service.DefaultCommunicationsInput() && OutputBefore == service.DefaultOutput() && CommunicationsOutputBefore == service.DefaultCommunicationsOutput();
}

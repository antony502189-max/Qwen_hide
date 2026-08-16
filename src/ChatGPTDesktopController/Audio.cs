using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Collections.Concurrent;

namespace ChatGPTDesktopController;

public sealed record AudioEndpoint(string Id, string Name);

public sealed class AudioDeviceService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    public IReadOnlyList<AudioEndpoint> Inputs() => Endpoints(DataFlow.Capture);
    public IReadOnlyList<AudioEndpoint> Outputs() => Endpoints(DataFlow.Render);
    public string? DefaultInput() => Default(DataFlow.Capture, Role.Multimedia);
    public string? DefaultOutput() => Default(DataFlow.Render, Role.Multimedia);
    public string? DefaultCommunicationsInput() => Default(DataFlow.Capture, Role.Communications);
    public string? DefaultCommunicationsOutput() => Default(DataFlow.Render, Role.Communications);
    public MMDevice? Find(string? id, DataFlow flow)
    {
        try { return string.IsNullOrWhiteSpace(id) ? _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia) : _enumerator.GetDevice(id); }
        catch { return null; }
    }
    public bool ValidateVirtualMixOutput(string? virtualOutput, string? loopbackSource, out string reason)
    {
        if (string.IsNullOrWhiteSpace(virtualOutput)) { reason = "No virtual output selected"; return false; }
        if (string.Equals(virtualOutput, loopbackSource, StringComparison.OrdinalIgnoreCase)) { reason = "Virtual output cannot be the loopback source"; return false; }
        if (string.Equals(virtualOutput, DefaultOutput(), StringComparison.OrdinalIgnoreCase) || string.Equals(virtualOutput, DefaultCommunicationsOutput(), StringComparison.OrdinalIgnoreCase)) { reason = "Refusing to use a Windows default output"; return false; }
        var endpoint = Find(virtualOutput, DataFlow.Render);
        if (endpoint is null) { reason = "Selected virtual output is unavailable"; return false; }
        if (!VirtualMixOutputPolicy.IsRecognizedVirtualName(endpoint.FriendlyName)) { reason = "Selected output is not a recognized virtual endpoint"; return false; }
        reason = "Virtual output validated"; return true;
    }
    private IReadOnlyList<AudioEndpoint> Endpoints(DataFlow flow) => _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active).Select(x => new AudioEndpoint(x.ID, x.FriendlyName)).ToList();
    private string? Default(DataFlow flow, Role role) { try { return _enumerator.GetDefaultAudioEndpoint(flow, role).ID; } catch { return null; } }
    public void Dispose() => _enumerator.Dispose();
}

public static class VirtualMixOutputPolicy
{
    public static bool IsRecognizedVirtualName(string? name) => !string.IsNullOrWhiteSpace(name) && new[] { "vb-audio", "cable input", "voicemeeter", "virtual cable", "virtual audio" }.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase));
}

public sealed class AudioDefaultDeviceGuard
{
    private readonly string? _input, _communicationsInput, _output, _communicationsOutput;
    public AudioDefaultDeviceGuard(AudioDeviceService devices) { _input = devices.DefaultInput(); _communicationsInput = devices.DefaultCommunicationsInput(); _output = devices.DefaultOutput(); _communicationsOutput = devices.DefaultCommunicationsOutput(); }
    public bool Verify(AudioDeviceService devices) => _input == devices.DefaultInput() && _communicationsInput == devices.DefaultCommunicationsInput() && _output == devices.DefaultOutput() && _communicationsOutput == devices.DefaultCommunicationsOutput();
}

public sealed class MixedAudioSession : IDisposable
{
    private const int Rate = 48000, PumpSamples = 480, MaximumQueued = Rate / 2, TrimTo = Rate / 4;
    private readonly AudioDeviceService _devices; private readonly AppLogger _log; private readonly ConcurrentQueue<float> _micQueue = new(), _systemQueue = new(); private readonly object _gate = new();
    private WasapiCapture? _mic; private WasapiLoopbackCapture? _loopback; private WasapiOut? _output; private BufferedWaveProvider? _provider; private System.Threading.Timer? _timer; private int _micCount, _systemCount, _generation, _pumping;
    public bool Running { get; private set; }
    public string Status { get; private set; } = "Idle";
    public string Microphone { get; private set; } = "Not configured";
    public string Loopback { get; private set; } = "Not configured";
    public string VirtualOutput { get; private set; } = "Not configured";
    public MixedAudioSession(AudioDeviceService devices, AppLogger log) { _devices = devices; _log = log; }
    public void Start(ControllerSettings settings)
    {
        lock (_gate)
        {
            StopCore(); var loopbackId = string.IsNullOrWhiteSpace(settings.LoopbackDeviceId) ? _devices.DefaultOutput() : settings.LoopbackDeviceId;
            if (!_devices.ValidateVirtualMixOutput(settings.VirtualOutputId, loopbackId, out var reason)) { Status = "Refused: " + reason; _log.Info("Audio mix " + Status); return; }
            var micDevice = _devices.Find(settings.PhysicalMicrophoneId, DataFlow.Capture); var loopbackDevice = _devices.Find(loopbackId, DataFlow.Render); var virtualDevice = _devices.Find(settings.VirtualOutputId, DataFlow.Render);
            if (micDevice is null || loopbackDevice is null || virtualDevice is null) { Status = "Refused: selected endpoint disappeared"; return; }
            var guard = new AudioDefaultDeviceGuard(_devices); var generation = ++_generation;
            try
            {
                _mic = new WasapiCapture(micDevice); var micFormat = _mic.WaveFormat; _mic.DataAvailable += (_, e) => { if (generation == _generation) Enqueue(_micQueue, ref _micCount, Convert(e.Buffer, e.BytesRecorded, micFormat)); }; _mic.StartRecording(); Microphone = "READY: " + micDevice.FriendlyName;
                _loopback = new WasapiLoopbackCapture(loopbackDevice); var loopFormat = _loopback.WaveFormat; _loopback.DataAvailable += (_, e) => { if (generation == _generation) Enqueue(_systemQueue, ref _systemCount, Convert(e.Buffer, e.BytesRecorded, loopFormat)); }; _loopback.StartRecording(); Loopback = "READY: " + loopbackDevice.FriendlyName;
                _provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1)) { BufferDuration = TimeSpan.FromMilliseconds(750), DiscardOnBufferOverflow = true };
                _output = new WasapiOut(virtualDevice, AudioClientShareMode.Shared, true, 40); _output.Init(_provider); _output.Play(); VirtualOutput = "READY: " + virtualDevice.FriendlyName;
                _timer = new System.Threading.Timer(_ => Pump(settings.MicrophoneGain, settings.SystemAudioGain, generation), null, 0, 10); Running = true; Status = guard.Verify(_devices) ? "READY: mixed stream is sent only to the selected virtual output" : "STOPPED: Windows default audio device changed";
                if (!Status.StartsWith("READY", StringComparison.Ordinal)) StopCore();
            }
            catch (Exception ex) { Status = "Unavailable: " + ex.GetType().Name; _log.Error("Audio mix startup failed: " + ex.GetType().Name); StopCore(); }
        }
    }
    public void Stop() { lock (_gate) StopCore(); }
    private void Pump(double micGain, double systemGain, int generation)
    {
        if (generation != Volatile.Read(ref _generation) || Interlocked.Exchange(ref _pumping, 1) != 0) return;
        try
        {
            var provider = _provider; if (provider is null) return; Trim(_micQueue, ref _micCount); Trim(_systemQueue, ref _systemCount); var mic = Dequeue(_micQueue, ref _micCount); var system = Dequeue(_systemQueue, ref _systemCount); var data = new float[PumpSamples];
            for (var i = 0; i < data.Length; i++) data[i] = MathF.Tanh((float)(mic[i] * Math.Clamp(micGain, 0, 4) + system[i] * Math.Clamp(systemGain, 0, 4)));
            var bytes = new byte[data.Length * sizeof(float)]; Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length); provider.AddSamples(bytes, 0, bytes.Length);
        }
        catch (Exception ex) { Status = "Stopped: " + ex.GetType().Name; _log.Error("Audio pump failed: " + ex.GetType().Name); }
        finally { Volatile.Write(ref _pumping, 0); }
    }
    private void StopCore()
    {
        ++_generation; try { _timer?.Dispose(); _mic?.StopRecording(); _loopback?.StopRecording(); _output?.Stop(); } catch { } _timer?.Dispose(); _mic?.Dispose(); _loopback?.Dispose(); _output?.Dispose(); _timer = null; _mic = null; _loopback = null; _output = null; _provider = null; _micQueue.Clear(); _systemQueue.Clear(); _micCount = _systemCount = 0; Running = false;
    }
    private static void Enqueue(ConcurrentQueue<float> queue, ref int count, float[] values) { Interlocked.Add(ref count, values.Length); foreach (var value in values) queue.Enqueue(value); Trim(queue, ref count); }
    private static void Trim(ConcurrentQueue<float> queue, ref int count) { while (Volatile.Read(ref count) > MaximumQueued && queue.TryDequeue(out _)) Interlocked.Decrement(ref count); while (Volatile.Read(ref count) > TrimTo && queue.TryDequeue(out _)) Interlocked.Decrement(ref count); }
    private static float[] Dequeue(ConcurrentQueue<float> queue, ref int count) { var result = new float[PumpSamples]; for (var i = 0; i < result.Length && queue.TryDequeue(out var value); i++) { result[i] = value; Interlocked.Decrement(ref count); } return result; }
    private static float[] Convert(byte[] buffer, int length, WaveFormat format)
    {
        var frames = length / Math.Max(1, format.BlockAlign); if (frames <= 0) return []; var mono = new float[frames]; var bytes = Math.Max(1, format.BitsPerSample / 8);
        for (var frame = 0; frame < frames; frame++) { float sum = 0; for (var channel = 0; channel < format.Channels; channel++) { var offset = frame * format.BlockAlign + channel * bytes; sum += Read(buffer, offset, format); } mono[frame] = sum / format.Channels; }
        if (format.SampleRate == Rate || mono.Length == 1) return mono; var output = new float[Math.Max(1, (int)Math.Round(mono.Length * (double)Rate / format.SampleRate))]; var ratio = format.SampleRate / (double)Rate;
        for (var i = 0; i < output.Length; i++) { var pos = Math.Min(mono.Length - 1d, i * ratio); var left = (int)pos; var right = Math.Min(mono.Length - 1, left + 1); output[i] = mono[left] + (mono[right] - mono[left]) * (float)(pos - left); } return output;
    }
    private static float Read(byte[] b, int o, WaveFormat f)
    {
        if (o < 0 || o >= b.Length) return 0; if (f.BitsPerSample == 16 && o + 1 < b.Length) return BitConverter.ToInt16(b, o) / 32768f; if (f.BitsPerSample == 32 && o + 3 < b.Length) return f.Encoding == WaveFormatEncoding.IeeeFloat ? Math.Clamp(BitConverter.ToSingle(b, o), -1, 1) : BitConverter.ToInt32(b, o) / 2147483648f; if (f.BitsPerSample == 8) return (b[o] - 128) / 128f; return 0;
    }
    public void Dispose() => Stop();
}

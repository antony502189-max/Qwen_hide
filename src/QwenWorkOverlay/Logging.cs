using System.Collections.Concurrent;
using System.Text;

namespace QwenWorkOverlay;

// Logging is deliberately lossy under load. A controller must never make Qwen or the shell wait
// for a disk write just to record a diagnostic message.
public sealed class AppLogger : IDisposable
{
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private const int Capacity = 1024;
    private readonly string _path;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _writer;
    private int _queued;
    private int _dropped;
    private int _disposed;

    public AppLogger()
    {
        var directory = Path.Combine(SettingsService.Root, "logs");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "app.log");
        _writer = Task.Run(WriteLoopAsync);
    }

    public string LogPath => _path;
    public int DroppedMessageCount => Volatile.Read(ref _dropped);
    public void Info(string text) => Enqueue("INFO", text);
    public void Error(string text) => Enqueue("ERROR", text);

    private void Enqueue(string level, string text)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (Interlocked.Increment(ref _queued) > Capacity)
        {
            Interlocked.Decrement(ref _queued);
            Interlocked.Increment(ref _dropped);
            return;
        }
        _queue.Enqueue($"{DateTimeOffset.Now:O} [{level}] {text}");
        _signal.Release();
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync(_stop.Token).ConfigureAwait(false);
                var batch = new StringBuilder();
                while (_queue.TryDequeue(out var entry))
                {
                    Interlocked.Decrement(ref _queued);
                    batch.AppendLine(entry);
                    if (batch.Length >= 16 * 1024) break;
                }
                if (batch.Length == 0) continue;
                RotateIfNeeded();
                await File.AppendAllTextAsync(_path, batch.ToString(), _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch { /* logging is best effort */ }
    }

    private void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.Length < MaxLogBytes) return;
            var rotated = _path + ".1";
            if (File.Exists(rotated)) File.Delete(rotated);
            File.Move(_path, rotated);
        }
        catch { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        try { _writer.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _signal.Dispose();
        _stop.Dispose();
    }
}

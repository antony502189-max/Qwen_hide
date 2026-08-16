using System.Collections.Concurrent;

namespace ChatGPTDesktopController;

public sealed class AppLogger
{
    private readonly ConcurrentQueue<string> _recent = new();
    private readonly string _path;
    public AppLogger()
    {
        AppPaths.Ensure();
        _path = Path.Combine(AppPaths.Logs, $"controller-{DateTime.UtcNow:yyyyMMdd}.log");
    }
    public IReadOnlyList<string> Recent => _recent.ToArray();
    public void Info(string message) => Write("INFO", message);
    public void Error(string message) => Write("ERROR", message);
    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:O} [{level}] {message}";
        _recent.Enqueue(line); while (_recent.Count > 250) _recent.TryDequeue(out _);
        try { File.AppendAllText(_path, line + Environment.NewLine); } catch { }
    }
}

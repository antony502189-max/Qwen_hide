namespace QwenWorkOverlay;

public sealed class AppLogger
{
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private readonly string _path;
    private readonly object _gate = new();

    public AppLogger()
    {
        var directory = Path.Combine(SettingsService.Root, "logs");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "app.log");
        RotateIfNeeded();
    }

    public string LogPath => _path;
    public void Info(string text) => Write("INFO", text);
    public void Error(string text) => Write("ERROR", text);

    private void Write(string level, string text)
    {
        try
        {
            lock (_gate)
            {
                RotateIfNeeded();
                File.AppendAllText(_path, $"{DateTimeOffset.Now:O} [{level}] {text}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never crash or alter Qwen/Windows behavior.
        }
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
        catch
        {
            // Logging is best-effort only.
        }
    }
}

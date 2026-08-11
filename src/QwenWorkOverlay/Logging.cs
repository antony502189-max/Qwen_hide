namespace QwenWorkOverlay;
public sealed class AppLogger
{
    private readonly string _path;
    public AppLogger() { var d = Path.Combine(SettingsService.Root, "logs"); Directory.CreateDirectory(d); _path = Path.Combine(d, "app.log"); }
    public void Info(string text) => Write("INFO", text);
    public void Error(string text) => Write("ERROR", text);
    private void Write(string level, string text) { try { File.AppendAllText(_path, $"{DateTimeOffset.Now:O} [{level}] {text}{Environment.NewLine}"); } catch { } }
}

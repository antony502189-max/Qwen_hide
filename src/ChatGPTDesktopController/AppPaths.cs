namespace ChatGPTDesktopController;

public static class AppPaths
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatGPTDesktopController");
    public static string Logs => Path.Combine(Root, "logs");
    public static string Settings => Path.Combine(Root, "settings.json");
    public static string RecoveryJournal => Path.Combine(Root, "window-recovery.json");
    public static void Ensure() { Directory.CreateDirectory(Root); Directory.CreateDirectory(Logs); }
}

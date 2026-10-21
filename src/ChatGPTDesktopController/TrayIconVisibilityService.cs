using System.Runtime.InteropServices;

namespace ChatGPTDesktopController;

internal sealed class TrayIconVisibilityService : IDisposable
{
    // Production GUID from the installed app's F3e(buildFlavor) Windows Tray identity.
    internal static readonly Guid ChatGptTrayGuid = new("e5768d8b-6936-4f45-b1ad-4c5fb414cb35");
    private readonly AppLogger _log;
    private IntPtr _owner;
    private bool _modified;
    private DateTimeOffset _lastAttempt;
    public bool LastSucceeded { get; private set; }
    public string Detail { get; private set; } = "Not attempted";

    public TrayIconVisibilityService(AppLogger log) => _log = log;

    public bool EnsureHidden(ChatGPTTarget target)
    {
        if (!CurrentAppPrivacyPolicy.IsInstalledTarget(target.ExecutablePath)) return false;
        if (DateTimeOffset.UtcNow - _lastAttempt < TimeSpan.FromSeconds(2)) return LastSucceeded;
        _lastAttempt = DateTimeOffset.UtcNow;
        _owner = Native.TopLevelWindows((uint)target.ProcessId)
            .FirstOrDefault(h => Native.WindowClass(h).Contains("NotifyIcon", StringComparison.OrdinalIgnoreCase));
        LastSucceeded = SetHidden(_owner, true);
        Detail = LastSucceeded ? "Windows accepted NIS_HIDDEN for the native ChatGPT tray icon" : "Native tray icon is absent or Windows rejected the state change";
        if (LastSucceeded && !_modified) _log.Info(Detail);
        _modified |= LastSucceeded;
        return LastSucceeded;
    }

    private static bool SetHidden(IntPtr owner, bool hidden)
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = owner,
            uFlags = 0x00000008 | 0x00000020, // NIF_STATE | NIF_GUID
            dwStateMask = 1, // NIS_HIDDEN: preserve every other bit, icon, callback and tooltip.
            dwState = hidden ? 1u : 0u,
            guidItem = ChatGptTrayGuid,
            szTip = string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
        return Shell_NotifyIcon(1, ref data); // NIM_MODIFY; never delete or recreate the app icon.
    }

    public void Restore()
    {
        if (!_modified) return;
        if (SetHidden(_owner, false)) _modified = false;
        else _log.Info("Native tray restore was unavailable; the app may have already exited");
    }

    public void Dispose() => Restore();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);
}

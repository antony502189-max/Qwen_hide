using System.Runtime.InteropServices;

namespace ChatGPTDesktopController;

internal sealed class TaskbarVisibilityService
{
    private readonly AppLogger _log;
    private readonly object _gate = new();
    private IntPtr _lastHwnd;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

    public bool LastSucceeded { get; private set; }
    public string Detail { get; private set; } = "Not attempted.";

    public TaskbarVisibilityService(AppLogger log) => _log = log;

    public bool EnsureHidden(IntPtr hwnd, bool force = false)
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd))
        {
            LastSucceeded = false;
            Detail = "Target HWND is unavailable.";
            return false;
        }

        lock (_gate)
        {
            if (!force && hwnd == _lastHwnd && DateTimeOffset.UtcNow - _lastAttempt < TimeSpan.FromSeconds(2))
                return LastSucceeded;

            _lastHwnd = hwnd;
            _lastAttempt = DateTimeOffset.UtcNow;
        }

        var ok = InvokeTaskbar(hwnd, delete: true, out var detail);
        LastSucceeded = ok;
        Detail = detail;
        if (!ok) _log.Error("Taskbar suppression failed: " + detail);
        return ok;
    }

    public bool Restore(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return false;

        var ok = InvokeTaskbar(hwnd, delete: false, out var detail);
        if (!ok) _log.Error("Taskbar restore failed: " + detail);
        return ok;
    }

    private static bool InvokeTaskbar(IntPtr hwnd, bool delete, out string detail)
    {
        object? raw = null;
        try
        {
            raw = new CTaskbarList();
            var taskbar = (ITaskbarList)raw;

            var hr = taskbar.HrInit();
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            hr = delete ? taskbar.DeleteTab(hwnd) : taskbar.AddTab(hwnd);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            detail = delete
                ? $"ChatGPT HWND 0x{hwnd.ToInt64():X} removed from taskbar."
                : $"ChatGPT HWND 0x{hwnd.ToInt64():X} restored to taskbar.";
            return true;
        }
        catch (COMException ex)
        {
            detail = $"COM 0x{ex.HResult:X8}";
            return false;
        }
        catch (Exception ex)
        {
            detail = ex.GetType().Name;
            return false;
        }
        finally
        {
            if (raw is not null && Marshal.IsComObject(raw)) Marshal.FinalReleaseComObject(raw);
        }
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11D0-958A-006097C9A090")]
    private sealed class CTaskbarList
    {
    }

    [ComImport]
    [Guid("56FDF342-FD6D-11D0-958A-006097C9A090")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(IntPtr hwnd);
        [PreserveSig] int DeleteTab(IntPtr hwnd);
        [PreserveSig] int ActivateTab(IntPtr hwnd);
        [PreserveSig] int SetActiveAlt(IntPtr hwnd);
    }
}

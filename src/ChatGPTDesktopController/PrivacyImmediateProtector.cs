using System.Runtime.InteropServices;

namespace ChatGPTDesktopController;

/// <summary>
/// Synchronous, narrowly-scoped protection used at lifecycle boundaries where waiting for an async
/// guard scan would create an avoidable exposure gap. It never changes visibility, z-order, styles,
/// opacity, click-through, focus, or hotkeys.
/// </summary>
internal static class PrivacyImmediateProtector
{
    private static readonly int[] RetryDelaysMilliseconds = [0, 25, 50, 100];
    private const int VisibleGateWaitMilliseconds = 25;
    private const uint VisibleRemoteWaitMilliseconds = 250;

    public static bool EnsureVerified(ChatGPTTarget? target, AppLogger log, string context)
    {
        if (target is null || target.Hwnd == IntPtr.Zero || !NativePrivacy.IsWindow(target.Hwnd)) return false;
        if (!string.Equals(target.Architecture, "x64", StringComparison.OrdinalIgnoreCase))
        {
            log.Error($"Privacy immediate protection ({context}) skipped: unsupported target architecture {target.Architecture}");
            return false;
        }

        if (NativePrivacy.DwmIsCompositionEnabled(out var composing) != 0 || !composing)
        {
            log.Error($"Privacy immediate protection ({context}) refused: DWM composition is unavailable");
            return false;
        }

        Native.GetWindowThreadProcessId(target.Hwnd, out var actualPid);
        if (actualPid != (uint)target.ProcessId)
        {
            log.Error($"Privacy immediate protection ({context}) refused: HWND owner changed");
            return false;
        }

        string lastDetail = "not attempted";
        foreach (var delay in RetryDelaysMilliseconds)
        {
            if (delay > 0) Thread.Sleep(delay);
            if (!NativePrivacy.IsWindow(target.Hwnd)) return false;
            if (NativePrivacy.DwmIsCompositionEnabled(out composing) != 0 || !composing)
            {
                lastDetail = "DWM composition became unavailable";
                break;
            }

            Native.GetWindowThreadProcessId(target.Hwnd, out actualPid);
            if (actualPid != (uint)target.ProcessId) return false;

            if (NativePrivacy.GetWindowDisplayAffinity(target.Hwnd, out var affinity) &&
                affinity == PrivacyGuardService.WdaExcludeFromCapture)
            {
                log.Info($"Privacy immediate protection ({context}) verified HWND 0x{target.Hwnd.ToInt64():X} at 0x11 with DWM active");
                return true;
            }

            // Visible transitions are fail-closed. Never wait behind a long-running background repair or
            // a multi-second remote call while private UI remains on screen. If this short owner-process
            // attempt cannot complete, the caller hides ChatGPT and the background guard may retry later.
            if (!RemoteDisplayAffinity.TrySet(
                    (uint)target.ProcessId,
                    target.Hwnd,
                    PrivacyGuardService.WdaExcludeFromCapture,
                    out lastDetail,
                    gateWaitMilliseconds: VisibleGateWaitMilliseconds,
                    remoteWaitMilliseconds: VisibleRemoteWaitMilliseconds))
            {
                continue;
            }

            if (NativePrivacy.DwmIsCompositionEnabled(out composing) == 0 && composing &&
                NativePrivacy.GetWindowDisplayAffinity(target.Hwnd, out affinity) &&
                affinity == PrivacyGuardService.WdaExcludeFromCapture)
            {
                log.Info($"Privacy immediate protection ({context}) applied+verified HWND 0x{target.Hwnd.ToInt64():X} with DWM active");
                return true;
            }

            var getterError = Marshal.GetLastWin32Error();
            lastDetail += $"; post-set runtime verification failed (win32={getterError})";
        }

        log.Error($"Privacy immediate protection ({context}) could not verify HWND 0x{target.Hwnd.ToInt64():X}: {lastDetail}");
        return false;
    }
}

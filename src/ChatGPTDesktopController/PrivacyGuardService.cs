using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ChatGPTDesktopController;

public enum PrivacyGuardState
{
    Waiting,
    Protected,
    Partial,
    Failed,
    Unsupported
}

public sealed record PrivacyGuardSnapshot(
    PrivacyGuardState State,
    bool DwmComposing,
    int WindowsSeen,
    int WindowsProtected,
    int WindowsFailed,
    string Detail,
    DateTimeOffset LastScanUtc)
{
    public static PrivacyGuardSnapshot Initial { get; } =
        new(PrivacyGuardState.Waiting, false, 0, 0, 0, "Waiting for ChatGPT Classic", DateTimeOffset.MinValue);
}

/// <summary>
/// Best-effort capture protection for the installed ChatGPT Classic window.
///
/// SetWindowDisplayAffinity can only be called by the process that owns the HWND. ChatGPT Classic
/// is a separate Electron process, so this service executes a tiny x64 call stub inside the owning
/// ChatGPT process. It does not patch binaries, scrape app data, hook Chromium, or alter the controller's
/// window-management logic. New/recreated top-level ChatGPT windows are re-protected automatically.
///
/// This is defense-in-depth, not a universal anti-capture guarantee. Windows itself documents display
/// affinity as protection against a set of public OS capture paths, not DRM/security against every
/// possible recorder.
/// </summary>
public sealed class PrivacyGuardService : IDisposable
{
    public const uint WdaNone = 0x00000000;
    public const uint WdaMonitor = 0x00000001;
    public const uint WdaExcludeFromCapture = 0x00000011;

    private readonly AppLogger _log;
    private readonly Timer _timer;
    private readonly NativePrivacy.WinEventDelegate _winEventDelegate;
    private readonly IntPtr _winEventHook;
    private readonly ConcurrentDictionary<long, string> _lastWindowStates = new();
    private int _scanActive;
    private int _disposed;
    private PrivacyGuardSnapshot _snapshot = PrivacyGuardSnapshot.Initial;

    public PrivacyGuardSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public PrivacyGuardService(AppLogger log)
    {
        _log = log;
        _winEventDelegate = OnWinEvent;
        _winEventHook = NativePrivacy.SetWinEventHook(
            NativePrivacy.EVENT_OBJECT_CREATE,
            NativePrivacy.EVENT_OBJECT_SHOW,
            IntPtr.Zero,
            _winEventDelegate,
            0,
            0,
            NativePrivacy.WINEVENT_OUTOFCONTEXT | NativePrivacy.WINEVENT_SKIPOWNPROCESS);

        if (_winEventHook == IntPtr.Zero)
            _log.Error("Privacy guard WinEvent hook unavailable; periodic protection remains active");

        // Event hook handles normal window creation quickly; periodic scan is a recovery backstop.
        _timer = new Timer(_ => QueueScan(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    public bool ProtectControllerOwnedWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativePrivacy.IsWindow(hwnd)) return false;
        if (!NativePrivacy.SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture))
        {
            _log.Error("Failed to protect controller-owned diagnostics window: win32=" + Marshal.GetLastWin32Error());
            return false;
        }
        return NativePrivacy.GetWindowDisplayAffinity(hwnd, out var affinity) && affinity == WdaExcludeFromCapture;
    }

    public void ScanNow() => QueueScan();

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (Volatile.Read(ref _disposed) != 0 || hwnd == IntPtr.Zero || idObject != NativePrivacy.OBJID_WINDOW || idChild != 0) return;
        // Queue outside the WinEvent callback. Never perform process injection on the UI/event callback thread.
        QueueScan();
    }

    private void QueueScan()
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _scanActive, 1) != 0) return;
        _ = Task.Run(() =>
        {
            try { ScanCore(); }
            catch (Exception ex)
            {
                _log.Error("Privacy guard scan failed: " + ex.GetType().Name);
                Volatile.Write(ref _snapshot, new PrivacyGuardSnapshot(
                    PrivacyGuardState.Failed, false, 0, 0, 0,
                    "Guard scan failed: " + ex.GetType().Name, DateTimeOffset.UtcNow));
            }
            finally { Volatile.Write(ref _scanActive, 0); }
        });
    }

    private void ScanCore()
    {
        var dwmComposing = NativePrivacy.DwmIsCompositionEnabled(out var composing) == 0 && composing;
        var windows = EnumerateChatGptTopLevelWindows();

        if (windows.Count == 0)
        {
            Volatile.Write(ref _snapshot, new PrivacyGuardSnapshot(
                PrivacyGuardState.Waiting, dwmComposing, 0, 0, 0,
                dwmComposing ? "Waiting for ChatGPT Classic windows" : "DWM composition unavailable",
                DateTimeOffset.UtcNow));
            return;
        }

        if (!Environment.Is64BitProcess)
        {
            Volatile.Write(ref _snapshot, new PrivacyGuardSnapshot(
                PrivacyGuardState.Unsupported, dwmComposing, windows.Count, 0, windows.Count,
                "Privacy guard requires the x64 controller build", DateTimeOffset.UtcNow));
            return;
        }

        var protectedCount = 0;
        var failedCount = 0;
        var failureDetails = new List<string>();

        foreach (var window in windows)
        {
            var result = ProtectWindow(window);
            if (result.Protected) protectedCount++;
            else
            {
                failedCount++;
                if (failureDetails.Count < 3) failureDetails.Add($"0x{window.Hwnd.ToInt64():X}: {result.Detail}");
            }

            var stateText = result.Protected ? "protected" : "FAILED " + result.Detail;
            var key = window.Hwnd.ToInt64();
            if (!_lastWindowStates.TryGetValue(key, out var previous) || !string.Equals(previous, stateText, StringComparison.Ordinal))
            {
                _lastWindowStates[key] = stateText;
                if (result.Protected) _log.Info($"Privacy guard protected ChatGPT HWND 0x{key:X} PID {window.ProcessId}");
                else _log.Error($"Privacy guard could not protect ChatGPT HWND 0x{key:X}: {result.Detail}");
            }
        }

        var activeHandles = windows.Select(x => x.Hwnd.ToInt64()).ToHashSet();
        foreach (var key in _lastWindowStates.Keys)
            if (!activeHandles.Contains(key)) _lastWindowStates.TryRemove(key, out _);

        PrivacyGuardState state;
        string detail;
        if (!dwmComposing)
        {
            state = PrivacyGuardState.Failed;
            detail = "DWM composition is not active; Windows display-affinity protection cannot be trusted";
        }
        else if (protectedCount == windows.Count)
        {
            state = PrivacyGuardState.Protected;
            detail = $"WDA_EXCLUDEFROMCAPTURE verified on {protectedCount}/{windows.Count} ChatGPT top-level window(s)";
        }
        else if (protectedCount > 0)
        {
            state = PrivacyGuardState.Partial;
            detail = $"Only {protectedCount}/{windows.Count} ChatGPT windows protected; " + string.Join(" | ", failureDetails);
        }
        else
        {
            state = PrivacyGuardState.Failed;
            detail = "No ChatGPT windows protected; " + string.Join(" | ", failureDetails);
        }

        Volatile.Write(ref _snapshot, new PrivacyGuardSnapshot(
            state, dwmComposing, windows.Count, protectedCount, failedCount, detail, DateTimeOffset.UtcNow));
    }

    private ProtectionResult ProtectWindow(ChatGptWindow window)
    {
        if (!NativePrivacy.IsWindow(window.Hwnd)) return new(false, "window disappeared");
        Native.GetWindowThreadProcessId(window.Hwnd, out var actualPid);
        if (actualPid != window.ProcessId) return new(false, "HWND owner changed");

        if (NativePrivacy.GetWindowDisplayAffinity(window.Hwnd, out var current) && current == WdaExcludeFromCapture)
            return new(true, "already protected");

        if (!string.Equals(window.Architecture, "x64", StringComparison.OrdinalIgnoreCase))
            return new(false, "unsupported target architecture " + window.Architecture);

        if (!RemoteDisplayAffinity.TrySet(window.ProcessId, window.Hwnd, WdaExcludeFromCapture, out var remoteDetail))
            return new(false, remoteDetail);

        // Verification is intentionally external. GetWindowDisplayAffinity may inspect another process,
        // so a successful value here proves that the target HWND now carries the requested affinity.
        if (!NativePrivacy.GetWindowDisplayAffinity(window.Hwnd, out var verified))
            return new(false, "remote call succeeded but affinity verification failed: win32=" + Marshal.GetLastWin32Error());
        if (verified != WdaExcludeFromCapture)
            return new(false, $"affinity verification mismatch 0x{verified:X}");

        return new(true, "verified");
    }

    private static List<ChatGptWindow> EnumerateChatGptTopLevelWindows()
    {
        var windows = new List<ChatGptWindow>();
        var processInfo = new Dictionary<uint, (bool Valid, string Architecture)>();

        NativePrivacy.EnumWindows((hwnd, _) =>
        {
            if (!NativePrivacy.IsWindow(hwnd)) return true;
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0 || pid == Environment.ProcessId) return true;

            if (!processInfo.TryGetValue(pid, out var info))
            {
                info = ValidateChatGptProcess(pid);
                processInfo[pid] = info;
            }
            if (!info.Valid) return true;

            windows.Add(new ChatGptWindow(hwnd, pid, info.Architecture));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private static (bool Valid, string Architecture) ValidateChatGptProcess(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (!process.ProcessName.Equals("ChatGPT Classic", StringComparison.OrdinalIgnoreCase)) return (false, "unknown");
            string? path;
            try { path = process.MainModule?.FileName; } catch { return (false, "unknown"); }
            if (!ChatGPTProcessLocator.IsChatGPTClassicExecutable(path)) return (false, "unknown");
            return (true, ChatGPTProcessLocator.DetectPortableExecutableArchitecture(path!));
        }
        catch { return (false, "unknown"); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer.Dispose();
        if (_winEventHook != IntPtr.Zero) NativePrivacy.UnhookWinEvent(_winEventHook);
        // Deliberately do NOT clear ChatGPT's affinity here. If the controller exits while a share is
        // active, keeping the protection on the existing ChatGPT HWND is safer than exposing it.
    }

    private readonly record struct ChatGptWindow(IntPtr Hwnd, uint ProcessId, string Architecture);
    private readonly record struct ProtectionResult(bool Protected, string Detail);
}

internal static class RemoteDisplayAffinity
{
    private const uint PROCESS_CREATE_THREAD = 0x0002;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint PAGE_EXECUTE_READ = 0x20;
    private const uint WAIT_OBJECT_0 = 0;
    private const uint TH32CS_SNAPMODULE = 0x00000008;
    private const uint TH32CS_SNAPMODULE32 = 0x00000010;

    public static bool TrySet(uint pid, IntPtr hwnd, uint affinity, out string detail)
    {
        detail = "unknown";
        var process = NativePrivacy.OpenProcess(
            PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
            false,
            pid);
        if (process == IntPtr.Zero)
        {
            detail = "OpenProcess failed: win32=" + Marshal.GetLastWin32Error();
            return false;
        }

        IntPtr remoteCode = IntPtr.Zero;
        IntPtr thread = IntPtr.Zero;
        var completed = false;
        try
        {
            var localUser32 = NativePrivacy.GetModuleHandle("user32.dll");
            var localFunction = localUser32 == IntPtr.Zero ? IntPtr.Zero : NativePrivacy.GetProcAddress(localUser32, "SetWindowDisplayAffinity");
            if (localUser32 == IntPtr.Zero || localFunction == IntPtr.Zero)
            {
                detail = "local SetWindowDisplayAffinity export unavailable";
                return false;
            }

            if (!TryFindRemoteModule(pid, "user32.dll", out var remoteUser32))
            {
                detail = "remote user32.dll not found";
                return false;
            }

            var rva = localFunction.ToInt64() - localUser32.ToInt64();
            var remoteFunction = new IntPtr(remoteUser32.ToInt64() + rva);
            var code = BuildX64CallStub(hwnd, affinity, remoteFunction);

            remoteCode = NativePrivacy.VirtualAllocEx(process, IntPtr.Zero, (nuint)code.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (remoteCode == IntPtr.Zero)
            {
                detail = "VirtualAllocEx failed: win32=" + Marshal.GetLastWin32Error();
                return false;
            }

            if (!NativePrivacy.WriteProcessMemory(process, remoteCode, code, (nuint)code.Length, out var written) || written != (nuint)code.Length)
            {
                detail = "WriteProcessMemory failed: win32=" + Marshal.GetLastWin32Error();
                return false;
            }

            if (!NativePrivacy.VirtualProtectEx(process, remoteCode, (nuint)code.Length, PAGE_EXECUTE_READ, out _))
            {
                detail = "VirtualProtectEx failed: win32=" + Marshal.GetLastWin32Error();
                return false;
            }
            NativePrivacy.FlushInstructionCache(process, remoteCode, (nuint)code.Length);

            thread = NativePrivacy.CreateRemoteThread(process, IntPtr.Zero, 0, remoteCode, IntPtr.Zero, 0, out _);
            if (thread == IntPtr.Zero)
            {
                detail = "CreateRemoteThread failed: win32=" + Marshal.GetLastWin32Error();
                return false;
            }

            var wait = NativePrivacy.WaitForSingleObject(thread, 2000);
            if (wait != WAIT_OBJECT_0)
            {
                detail = "remote call timeout/wait failure: 0x" + wait.ToString("X");
                return false;
            }
            completed = true;

            if (!NativePrivacy.GetExitCodeThread(thread, out var exitCode))
            {
                detail = "GetExitCodeThread failed: win32=" + Marshal.GetLastWin32Error();
                return false;
            }
            if (exitCode == 0)
            {
                detail = "SetWindowDisplayAffinity returned FALSE inside ChatGPT";
                return false;
            }

            detail = "remote SetWindowDisplayAffinity returned TRUE";
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
        finally
        {
            if (thread != IntPtr.Zero) NativePrivacy.CloseHandle(thread);
            // Never free executable memory while a timed-out remote thread could still be running in it.
            if (remoteCode != IntPtr.Zero && completed) NativePrivacy.VirtualFreeEx(process, remoteCode, 0, MEM_RELEASE);
            NativePrivacy.CloseHandle(process);
        }
    }

    internal static byte[] BuildX64CallStub(IntPtr hwnd, uint affinity, IntPtr function)
    {
        // Windows x64 ABI:
        //   RCX = HWND
        //   EDX = affinity
        //   RAX = SetWindowDisplayAffinity
        //   reserve 32-byte shadow space + alignment, call, return BOOL in EAX as thread exit code.
        var code = new List<byte>(40);
        code.AddRange([0x48, 0xB9]);
        code.AddRange(BitConverter.GetBytes(hwnd.ToInt64()));
        code.Add(0xBA);
        code.AddRange(BitConverter.GetBytes(affinity));
        code.AddRange([0x48, 0xB8]);
        code.AddRange(BitConverter.GetBytes(function.ToInt64()));
        code.AddRange([0x48, 0x83, 0xEC, 0x28]);
        code.AddRange([0xFF, 0xD0]);
        code.AddRange([0x48, 0x83, 0xC4, 0x28]);
        code.Add(0xC3);
        return code.ToArray();
    }

    private static bool TryFindRemoteModule(uint pid, string moduleName, out IntPtr baseAddress)
    {
        baseAddress = IntPtr.Zero;
        var snapshot = NativePrivacy.CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
        if (snapshot == NativePrivacy.InvalidHandleValue) return false;
        try
        {
            var entry = new NativePrivacy.MODULEENTRY32 { dwSize = (uint)Marshal.SizeOf<NativePrivacy.MODULEENTRY32>() };
            if (!NativePrivacy.Module32First(snapshot, ref entry)) return false;
            do
            {
                if (entry.szModule.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                {
                    baseAddress = entry.modBaseAddr;
                    return baseAddress != IntPtr.Zero;
                }
                entry.dwSize = (uint)Marshal.SizeOf<NativePrivacy.MODULEENTRY32>();
            } while (NativePrivacy.Module32Next(snapshot, ref entry));
            return false;
        }
        finally { NativePrivacy.CloseHandle(snapshot); }
    }
}

internal static class NativePrivacy
{
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const int OBJID_WINDOW = 0;
    public static readonly IntPtr InvalidHandleValue = new(-1);

    public delegate bool EnumWindowsDelegate(IntPtr hwnd, IntPtr lParam);
    public delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MODULEENTRY32
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
    }

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsDelegate callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("dwmapi.dll")] public static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);

    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, nuint size, uint allocationType, uint protect);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool VirtualProtectEx(IntPtr process, IntPtr address, nuint size, uint newProtect, out uint oldProtect);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool VirtualFreeEx(IntPtr process, IntPtr address, nuint size, uint freeType);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, nuint size, out nuint written);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool FlushInstructionCache(IntPtr process, IntPtr address, nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, nuint stackSize, IntPtr startAddress, IntPtr parameter, uint creationFlags, out uint threadId);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool Module32First(IntPtr snapshot, ref MODULEENTRY32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool Module32Next(IntPtr snapshot, ref MODULEENTRY32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string moduleName);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] public static extern IntPtr GetProcAddress(IntPtr module, string procName);
}

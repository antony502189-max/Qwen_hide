using System.Diagnostics;

namespace ChatGPTDesktopController;

public sealed record PrivacyTransitionSnapshot(
    bool TargetTracked,
    bool PrimaryVerified,
    string Affinity,
    long RepairRequests,
    DateTimeOffset LastVerifiedUtc,
    string Detail)
{
    public static PrivacyTransitionSnapshot Initial { get; } =
        new(false, false, "unknown", 0, DateTimeOffset.MinValue, "No primary target tracked");
}

public static class PrivacyTransitionPolicy
{
    public const uint RequiredAffinity = PrivacyGuardService.WdaExcludeFromCapture;

    public static bool IsVerified(bool getterSucceeded, uint affinity) =>
        getterSucceeded && affinity == RequiredAffinity;

    public static bool NeedsRepair(bool getterSucceeded, uint affinity) =>
        !getterSucceeded || affinity != RequiredAffinity;
}

/// <summary>
/// Coordinates short-lived privacy-sensitive target transitions without changing WindowController.
/// The full PrivacyGuardService owns application/repair of WDA. This class adds a cheap primary-HWND
/// watchdog, burst scans after visibility/lifecycle transitions, and a fail-closed verification wait
/// used by the user-triggered show path.
/// </summary>
public sealed class PrivacyTransitionCoordinator : IDisposable
{
    private static readonly int[] BurstAtMilliseconds = [0, 40, 100, 200, 400, 800, 1500];

    private readonly PrivacyGuardService _guard;
    private readonly AppLogger _log;
    private readonly Timer _watchdog;
    private long _primaryHwnd;
    private uint _primaryPid;
    private int _burstEpoch;
    private int _disposed;
    private long _repairRequests;
    private long _lastRepairRequestTicks;
    private PrivacyTransitionSnapshot _snapshot = PrivacyTransitionSnapshot.Initial;

    public PrivacyTransitionSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public PrivacyTransitionCoordinator(PrivacyGuardService guard, AppLogger log)
    {
        _guard = guard;
        _log = log;
        // This timer only reads affinity for one HWND. Expensive process enumeration/repair remains
        // inside PrivacyGuardService and is requested only when the primary state is not verified.
        _watchdog = new Timer(_ => WatchdogTick(), null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
    }

    public void TrackPrimaryTarget(ChatGPTTarget? target)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (target is null)
        {
            Interlocked.Exchange(ref _primaryHwnd, 0);
            Volatile.Write(ref _primaryPid, 0);
            Volatile.Write(ref _snapshot, PrivacyTransitionSnapshot.Initial);
            return;
        }

        Interlocked.Exchange(ref _primaryHwnd, target.Hwnd.ToInt64());
        Volatile.Write(ref _primaryPid, target.ProcessId);
        Volatile.Write(ref _snapshot, new PrivacyTransitionSnapshot(
            true, false, "checking", Interlocked.Read(ref _repairRequests), DateTimeOffset.MinValue,
            $"Tracking HWND 0x{target.Hwnd.ToInt64():X} PID {target.ProcessId}"));
        StartBurst();
    }

    public void NotifyVisibilityOrLifecycleTransition()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        StartBurst();
    }

    public async Task<bool> EnsureVerifiedAfterShowAsync(IntPtr hwnd, TimeSpan timeout)
    {
        if (hwnd == IntPtr.Zero || Volatile.Read(ref _disposed) != 0) return false;
        StartBurst();

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout && Volatile.Read(ref _disposed) == 0)
        {
            if (!NativePrivacy.IsWindow(hwnd)) return false;
            if (ReadVerified(hwnd, out var affinity)) return true;

            RequestRepair("show verification");
            try { await Task.Delay(25).ConfigureAwait(false); }
            catch (TaskCanceledException) { return false; }
        }

        var verified = ReadVerified(hwnd, out _);
        if (!verified)
            _log.Error($"Privacy fail-closed verification timed out for HWND 0x{hwnd.ToInt64():X} after {stopwatch.ElapsedMilliseconds} ms");
        return verified;
    }

    private void WatchdogTick()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        var hwndValue = Interlocked.Read(ref _primaryHwnd);
        if (hwndValue == 0) return;

        var hwnd = new IntPtr(hwndValue);
        if (!NativePrivacy.IsWindow(hwnd))
        {
            Volatile.Write(ref _snapshot, new PrivacyTransitionSnapshot(
                true, false, "window-gone", Interlocked.Read(ref _repairRequests), Snapshot.LastVerifiedUtc,
                "Tracked primary HWND no longer exists; waiting for controller reacquire"));
            return;
        }

        Native.GetWindowThreadProcessId(hwnd, out var pid);
        var expectedPid = Volatile.Read(ref _primaryPid);
        if (pid == 0 || pid != expectedPid)
        {
            Volatile.Write(ref _snapshot, new PrivacyTransitionSnapshot(
                true, false, "owner-mismatch", Interlocked.Read(ref _repairRequests), Snapshot.LastVerifiedUtc,
                $"Tracked HWND owner changed: expected {expectedPid}, actual {pid}"));
            RequestRepair("primary owner transition");
            return;
        }

        var getterSucceeded = NativePrivacy.GetWindowDisplayAffinity(hwnd, out var affinity);
        if (PrivacyTransitionPolicy.IsVerified(getterSucceeded, affinity))
        {
            RecordVerified(hwnd, affinity);
            return;
        }

        var affinityText = getterSucceeded ? $"0x{affinity:X}" : $"unreadable(win32={System.Runtime.InteropServices.Marshal.GetLastWin32Error()})";
        Volatile.Write(ref _snapshot, new PrivacyTransitionSnapshot(
            true, false, affinityText, Interlocked.Read(ref _repairRequests), Snapshot.LastVerifiedUtc,
            $"Primary capture affinity is not verified on HWND 0x{hwnd.ToInt64():X}; repair requested"));
        RequestRepair("primary watchdog");
    }

    private bool ReadVerified(IntPtr hwnd, out uint affinity)
    {
        var getterSucceeded = NativePrivacy.GetWindowDisplayAffinity(hwnd, out affinity);
        if (!PrivacyTransitionPolicy.IsVerified(getterSucceeded, affinity)) return false;
        RecordVerified(hwnd, affinity);
        return true;
    }

    private void RecordVerified(IntPtr hwnd, uint affinity)
    {
        var now = DateTimeOffset.UtcNow;
        Volatile.Write(ref _snapshot, new PrivacyTransitionSnapshot(
            true, true, $"0x{affinity:X}", Interlocked.Read(ref _repairRequests), now,
            $"Primary HWND 0x{hwnd.ToInt64():X} externally verified at WDA_EXCLUDEFROMCAPTURE"));
    }

    private void RequestRepair(string reason)
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        // Avoid turning a persistent failure into a tight injection loop. The underlying guard also
        // serializes scans, while burst scheduling provides low-latency retries around known transitions.
        var now = Stopwatch.GetTimestamp();
        var previous = Interlocked.Read(ref _lastRepairRequestTicks);
        var minimumTicks = Math.Max(1L, Stopwatch.Frequency / 20); // 50 ms
        if (previous != 0 && now - previous < minimumTicks) return;
        if (Interlocked.CompareExchange(ref _lastRepairRequestTicks, now, previous) != previous) return;

        Interlocked.Increment(ref _repairRequests);
        _guard.ScanNow();
        _log.Info("Privacy repair requested: " + reason);
    }

    private void StartBurst()
    {
        var epoch = Interlocked.Increment(ref _burstEpoch);
        _ = Task.Run(async () =>
        {
            var previous = 0;
            foreach (var at in BurstAtMilliseconds)
            {
                var delay = at - previous;
                previous = at;
                if (delay > 0)
                {
                    try { await Task.Delay(delay).ConfigureAwait(false); }
                    catch (TaskCanceledException) { return; }
                }

                if (Volatile.Read(ref _disposed) != 0 || epoch != Volatile.Read(ref _burstEpoch)) return;
                RequestRepair("transition burst");
            }
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Increment(ref _burstEpoch);
        _watchdog.Dispose();
    }
}

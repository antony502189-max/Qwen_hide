using System.Diagnostics;

namespace QwenWorkOverlay;

public sealed record ControllerRuntimeOptions(bool SafeMode, int? ExitAfterSeconds, bool OpacityEnabled)
{
    public static ControllerRuntimeOptions FromArguments(IEnumerable<string> arguments)
    {
        var values = arguments.ToArray();
        var index = Array.FindIndex(values, x => string.Equals(x, "--exit-after-seconds", StringComparison.OrdinalIgnoreCase));
        int? exitAfter = index >= 0 && index + 1 < values.Length && int.TryParse(values[index + 1], out var seconds) && seconds > 0 ? seconds : null;
        return new(
            values.Any(x => string.Equals(x, "--safe-mode", StringComparison.OrdinalIgnoreCase)),
            exitAfter,
            values.Any(x => string.Equals(x, "--enable-opacity", StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed class QwenSessionMonitor : IDisposable
{
    private readonly Func<QwenTarget?> _attached;
    private readonly Func<QwenTarget?> _discover;
    private readonly Action<QwenTarget?> _result;
    private readonly System.Threading.Timer _timer;
    private int _checking;
    private int _disposed;

    public QwenSessionMonitor(Func<QwenTarget?> attached, Func<QwenTarget?> discover, Action<QwenTarget?> result)
    {
        _attached = attached;
        _discover = discover;
        _result = result;
        _timer = new System.Threading.Timer(_ => QueueCheck(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Start() { QueueCheck(); _timer.Change(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)); }
    public void CheckNow() => QueueCheck();

    private void QueueCheck()
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _checking, 1) != 0) return;
        ThreadPool.UnsafeQueueUserWorkItem(_ =>
        {
            try
            {
                var attached = _attached();
                if (attached is not null && IsAlive(attached)) return;
                _result(_discover());
            }
            finally { Volatile.Write(ref _checking, 0); }
        }, null);
    }

    private static bool IsAlive(QwenTarget target)
    {
        try
        {
            using var process = Process.GetProcessById(target.ProcessId);
            return !process.HasExited && Native.IsWindow(target.Hwnd);
        }
        catch { return false; }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer.Dispose();
    }
}

public sealed record ProcessDiagnostics(int? Pid, double CpuPercent, long WorkingSetBytes, int ThreadCount, int HandleCount, string State);

public sealed class DiagnosticsService
{
    public async Task<(ProcessDiagnostics Controller, ProcessDiagnostics Qwen)> CollectAsync(QwenTarget? target, CancellationToken cancellationToken)
    {
        var controllerTask = SampleAsync(Environment.ProcessId, cancellationToken);
        var qwenTask = target is null ? Task.FromResult(new ProcessDiagnostics(null, 0, 0, 0, 0, "not attached")) : SampleAsync(target.ProcessId, cancellationToken);
        await Task.WhenAll(controllerTask, qwenTask).ConfigureAwait(false);
        return (await controllerTask.ConfigureAwait(false), await qwenTask.ConfigureAwait(false));
    }

    private static async Task<ProcessDiagnostics> SampleAsync(int processId, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited) return new(processId, 0, 0, 0, 0, "exited");
            var cpu0 = process.TotalProcessorTime;
            var wall0 = Stopwatch.GetTimestamp();
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            process.Refresh();
            var elapsed = (Stopwatch.GetTimestamp() - wall0) / (double)Stopwatch.Frequency;
            var cpu = elapsed <= 0 ? 0 : Math.Max(0, (process.TotalProcessorTime - cpu0).TotalSeconds / (elapsed * Environment.ProcessorCount) * 100);
            return new(processId, cpu, process.WorkingSet64, process.Threads.Count, process.HandleCount, process.HasExited ? "exited" : "running");
        }
        catch (Exception ex) { return new(processId, 0, 0, 0, 0, ex.GetType().Name); }
    }
}

public sealed class EmergencyRecoveryService
{
    private readonly Func<bool> _recover;
    private readonly Action _freezeMutations;
    private readonly Action _terminate;
    private readonly AppLogger _log;
    private int _requested;

    public EmergencyRecoveryService(WindowRecoveryService recovery, AppLogger log, Action? freezeMutations = null, Action? terminate = null)
    {
        _recover = recovery.TryRecoverStaleState;
        _freezeMutations = freezeMutations ?? (() => { });
        _terminate = terminate ?? (() => Environment.Exit(0));
        _log = log;
    }

    internal EmergencyRecoveryService(Func<bool> recover, AppLogger log, Action freezeMutations, Action terminate)
    {
        _recover = recover;
        _freezeMutations = freezeMutations;
        _terminate = terminate;
        _log = log;
    }

    public void RequestExit()
    {
        if (Interlocked.Exchange(ref _requested, 1) != 0) return;
        ThreadPool.UnsafeQueueUserWorkItem(_ =>
        {
            try { _freezeMutations(); } catch { }
            try { _recover(); } catch { }
            _log.Info("Emergency recovery requested; journal restoration completed before process exit");
            _terminate();
        }, null);
    }
}

public static class StaWork
{
    public static Task<T> RunAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(work()); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "QDC.STA.Worker" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}

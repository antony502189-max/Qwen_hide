using System.Diagnostics;

namespace QwenWorkOverlay;

/// <summary>
/// Bounded result returned by one of the packaged native capture diagnostics. The helper processes
/// never write frames or screenshots; their single stdout line contains aggregate pixel statistics.
/// </summary>
public sealed record NativeCaptureProbeResult(CaptureProbeVerdict Verdict, string Detail)
{
    public static readonly NativeCaptureProbeResult NotRun = new(CaptureProbeVerdict.NotRun, "Not run");
}

public static class NativeCaptureProbeOutputParser
{
    public static NativeCaptureProbeResult Parse(string output, string expectedPrefix)
    {
        var line = output.Trim();
        if (!line.StartsWith(expectedPrefix, StringComparison.Ordinal))
            return new(CaptureProbeVerdict.Failed, "Helper returned an invalid result");

        var token = line[expectedPrefix.Length..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var verdict = token switch
        {
            // Even if an older bundled helper labels a zero-difference sample LIKELY_EXCLUDED,
            // preserve the fail-safe controller policy: equality alone cannot establish absence.
            "LIKELY_EXCLUDED" => CaptureProbeVerdict.Inconclusive,
            "REDACTED_PLACEHOLDER" => CaptureProbeVerdict.RedactedPlaceholder,
            "EXPOSED" => CaptureProbeVerdict.Exposed,
            "INCONCLUSIVE" => CaptureProbeVerdict.Inconclusive,
            "FAILED" => CaptureProbeVerdict.Failed,
            _ => CaptureProbeVerdict.Failed
        };
        // Limit externally-produced diagnostic text even though the bundled helpers only emit one
        // aggregate-statistics line. It must never become a channel for Qwen/UI content.
        return new(verdict, line.Length <= 512 ? line : line[..512]);
    }
}

internal static class NativeCaptureProbeRunner
{
    public static async Task<NativeCaptureProbeResult> RunAsync(string helperFileName, string expectedPrefix, IntPtr hostHwnd)
    {
        if (hostHwnd == IntPtr.Zero || !Native.IsWindow(hostHwnd) || !Native.IsWindowVisible(hostHwnd))
            return new(CaptureProbeVerdict.Failed, "Probe requires an active visible privacy host");

        if (!string.Equals(Path.GetFileName(helperFileName), helperFileName, StringComparison.Ordinal))
            return new(CaptureProbeVerdict.Failed, "Invalid packaged helper name");
        var helperPath = Path.Combine(AppContext.BaseDirectory, helperFileName);
        if (!File.Exists(helperPath))
            return new(CaptureProbeVerdict.Failed, "Packaged helper is unavailable: " + helperFileName);

        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo(helperPath, "0x" + hostHwnd.ToInt64().ToString("X"))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process is null) return new(CaptureProbeVerdict.Failed, "Could not start packaged helper");

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12)).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            EnsureHostVisible(hostHwnd);
            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(error) ? "helper exit=" + process.ExitCode : "helper stderr";
                return new(CaptureProbeVerdict.Failed, detail);
            }
            return NativeCaptureProbeOutputParser.Parse(output, expectedPrefix);
        }
        catch (TimeoutException)
        {
            TryStop(process);
            EnsureHostVisible(hostHwnd);
            return new(CaptureProbeVerdict.Failed, "Packaged helper timed out");
        }
        catch (Exception ex)
        {
            TryStop(process);
            EnsureHostVisible(hostHwnd);
            return new(CaptureProbeVerdict.Failed, "Packaged helper failed: " + ex.GetType().Name);
        }
        finally { process?.Dispose(); }
    }

    private static void EnsureHostVisible(IntPtr hostHwnd)
    {
        if (Native.IsWindow(hostHwnd)) Native.ShowWindowAsync(hostHwnd, Native.SW_SHOWNOACTIVATE);
    }

    private static void TryStop(Process? process)
    {
        try
        {
            if (process is not null && !process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch { }
    }
}

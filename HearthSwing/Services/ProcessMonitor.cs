using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HearthSwing.Services;

public sealed class ProcessMonitor
{
    private const string WowProcessName = "WowClassic";
    private const string WowExeName = "WowClassic.exe";

    public event Action<string>? Log;

    public bool IsWowRunning()
    {
        return Process.GetProcessesByName(WowProcessName).Length > 0;
    }

    public void LaunchWow(string gamePath)
    {
        var exePath = Path.Combine(gamePath, WowExeName);
        if (!File.Exists(exePath))
            throw new FileNotFoundException($"WoW executable not found: {exePath}");

        Log?.Invoke($"Launching {WowExeName}...");
        Process.Start(
            new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = gamePath,
                UseShellExecute = true,
            }
        );
    }

    /// <summary>
    /// Waits for the WoW process to exit, checking every 2 seconds.
    /// </summary>
    public async Task WaitForExitAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var procs = Process.GetProcessesByName(WowProcessName);
            if (procs.Length == 0)
                break;

            foreach (var p in procs)
                p.Dispose();
            await Task.Delay(2000, ct);
        }
    }

    /// <summary>
    /// Waits for the specified delay, then invokes the callback. Cancellable.
    /// </summary>
    public async Task DelayedActionAsync(
        int delaySeconds,
        Action action,
        CancellationToken ct = default
    )
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            if (!ct.IsCancellationRequested)
                action();
        }
        catch (OperationCanceledException) { }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HearthSwing.Services;

public sealed class ProcessMonitor : IProcessMonitor
{
    private const string WowProcessName = "WowClassic";
    private const string WowExeName = "WowClassic.exe";
    private readonly IProcessManager _processManager;
    private readonly IFileSystem _fs;

    public ProcessMonitor(IProcessManager processManager, IFileSystem fileSystem)
    {
        _processManager = processManager;
        _fs = fileSystem;
    }

    public event Action<string>? Log;

    public bool IsWowRunning()
    {
        return _processManager.GetProcessesByName(WowProcessName).Length > 0;
    }

    public void LaunchWow(string gamePath)
    {
        var exePath = Path.Combine(gamePath, WowExeName);
        if (!_fs.FileExists(exePath))
            throw new FileNotFoundException($"WoW executable not found: {exePath}");

        Log?.Invoke($"Launching {WowExeName}...");
        _processManager.Start(
            new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = gamePath,
                UseShellExecute = true,
            }
        );
    }

    public async Task WaitForExitAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var procs = _processManager.GetProcessesByName(WowProcessName);
            if (procs.Length == 0)
                break;

            foreach (var p in procs)
                p.Dispose();
            await Task.Delay(2000, ct);
        }
    }
}

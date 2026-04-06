using System;
using System.Threading;
using System.Threading.Tasks;

namespace HearthSwing.Services;

public interface IProcessMonitor
{
    event Action<string>? Log;
    bool IsWowRunning();
    void LaunchWow(string gamePath);
    Task WaitForExitAsync(CancellationToken ct = default);
}

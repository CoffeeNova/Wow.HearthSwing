namespace HearthSwing.Services;

public interface IProcessMonitor
{
    bool IsWowRunning();
    void LaunchWow(string gamePath);
    Task WaitForExitAsync(CancellationToken ct = default);
}

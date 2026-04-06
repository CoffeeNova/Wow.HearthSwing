using System.Diagnostics;

namespace HearthSwing.Services;

public sealed class SystemProcessManager : IProcessManager
{
    public Process[] GetProcessesByName(string name) => Process.GetProcessesByName(name);

    public Process? Start(ProcessStartInfo startInfo) => Process.Start(startInfo);
}

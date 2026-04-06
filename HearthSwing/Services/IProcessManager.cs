using System.Diagnostics;

namespace HearthSwing.Services;

public interface IProcessManager
{
    Process[] GetProcessesByName(string name);
    Process? Start(ProcessStartInfo startInfo);
}

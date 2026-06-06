using HearthSwing.Models.WoW;

namespace HearthSwing.Services;

public interface IWtfInspector
{
    WowInstallation Inspect(string gamePath);
}

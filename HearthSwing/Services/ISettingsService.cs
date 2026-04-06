using HearthSwing.Models;

namespace HearthSwing.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    void Load();
    void Save();
}

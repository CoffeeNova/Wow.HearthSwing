using HearthSwing.Models.Profiles;
using System;
using System.Collections.Generic;

namespace HearthSwing.Services;

public interface ICacheProtector : IDisposable
{
    bool IsLocked { get; }
    int ProtectedFileCount { get; }
    List<string> CollectCacheFiles(
        string wtfPath,
        ProfileGranularity granularity = ProfileGranularity.FullWtf,
        string? accountName = null,
        string? realmName = null,
        string? characterName = null
    );
    void Lock(
        string wtfPath,
        ProfileGranularity granularity = ProfileGranularity.FullWtf,
        string? accountName = null,
        string? realmName = null,
        string? characterName = null
    );
    void Unlock();
    void ForceRestore(string wtfPath);
}

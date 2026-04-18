using System;
using System.Collections.Generic;

namespace HearthSwing.Services;

public interface ICacheProtector : IDisposable
{
    bool IsLocked { get; }
    int ProtectedFileCount { get; }
    List<string> CollectCacheFiles(string wtfPath);
    void Lock(string wtfPath);
    void Unlock();
    void ForceRestore(string wtfPath);
}

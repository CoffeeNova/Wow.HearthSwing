namespace HearthSwing.Services;

public static class CacheFilePatterns
{
    public static readonly string[] All =
    [
        "bindings-cache.wtf",
        "config-cache.wtf",
        "macros-cache.txt",
        "edit-mode-cache-account.txt",
        "edit-mode-cache-character.txt",
        "tts-cache-account.txt",
        "tts-cache-character.txt",
        "chat-cache.txt",
        "chat-frontend-cache.txt",
        "flagged-cache-account.txt",
        "layout-local.txt",
        "cache.md5",
    ];

    public static readonly string[] Tokenizable =
    [
        "bindings-cache.wtf",
        "config-cache.wtf",
        "macros-cache.txt",
        "edit-mode-cache-account.txt",
        "edit-mode-cache-character.txt",
        "tts-cache-account.txt",
        "tts-cache-character.txt",
        "chat-cache.txt",
        "chat-frontend-cache.txt",
        "flagged-cache-account.txt",
        "layout-local.txt",
    ];

    private static readonly HashSet<string> TokenizableCacheFiles = new(
        Tokenizable,
        StringComparer.OrdinalIgnoreCase
    );

    public static bool IsTokenizableCacheFileName(string fileName)
    {
        return TokenizableCacheFiles.Contains(fileName);
    }
}

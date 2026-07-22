using System.IO;

namespace HearthSwing.Services;

/// <summary>
/// Tokenizes all <c>*.lua</c> SavedVariables plus a fixed allowlist of text cache files.
/// <c>cache.md5</c> is intentionally excluded (a checksum WoW recomputes on its own).
/// </summary>
public sealed class TemplateFileClassifier : ITemplateFileClassifier
{
    private const string LuaExtension = ".lua";

    private static readonly HashSet<string> TokenizableCacheFiles = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "macros-cache.txt",
        "bindings-cache.wtf",
        "config-cache.wtf",
        "chat-cache.txt",
        "chat-frontend-cache.txt",
        "edit-mode-cache-account.txt",
        "edit-mode-cache-character.txt",
        "tts-cache-account.txt",
        "tts-cache-character.txt",
        "flagged-cache-account.txt",
        "layout-local.txt",
    };

    public bool ShouldTokenize(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        var fileName = Path.GetFileName(relativePath);

        if (Path.GetExtension(fileName).Equals(LuaExtension, StringComparison.OrdinalIgnoreCase))
            return true;

        return TokenizableCacheFiles.Contains(fileName);
    }
}

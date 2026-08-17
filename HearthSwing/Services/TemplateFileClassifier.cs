using System.IO;

namespace HearthSwing.Services;

/// <summary>
/// Tokenizes all <c>*.lua</c> SavedVariables plus a fixed allowlist of text cache files.
/// <c>cache.md5</c> is intentionally excluded (a checksum WoW recomputes on its own).
/// </summary>
public sealed class TemplateFileClassifier : ITemplateFileClassifier
{
    private const string LuaExtension = ".lua";

    public bool ShouldTokenize(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        var fileName = Path.GetFileName(relativePath);

        if (Path.GetExtension(fileName).Equals(LuaExtension, StringComparison.OrdinalIgnoreCase))
            return true;

        return CacheFilePatterns.IsTokenizableCacheFileName(fileName);
    }
}

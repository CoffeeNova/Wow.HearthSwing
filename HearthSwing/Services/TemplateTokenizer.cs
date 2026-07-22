namespace HearthSwing.Services;

/// <summary>
/// Ordinal, case-sensitive tokenizer. Replacement order minimizes false matches: the composite
/// "&lt;char&gt; - &lt;realm&gt;" key is handled before the standalone realm, which is handled
/// before the standalone character name.
/// </summary>
public sealed class TemplateTokenizer : ITemplateTokenizer
{
    public const string CharToken = "{{CHAR}}";
    public const string RealmToken = "{{REALM}}";

    public string Tokenize(string content, string charName, string realmName)
    {
        ArgumentNullException.ThrowIfNull(content);

        var result = content;

        if (!string.IsNullOrEmpty(charName) && !string.IsNullOrEmpty(realmName))
        {
            result = result.Replace(
                $"{charName} - {realmName}",
                $"{CharToken} - {RealmToken}",
                StringComparison.Ordinal
            );
        }

        if (!string.IsNullOrEmpty(realmName))
            result = result.Replace(realmName, RealmToken, StringComparison.Ordinal);

        if (!string.IsNullOrEmpty(charName))
            result = result.Replace(charName, CharToken, StringComparison.Ordinal);

        return result;
    }

    public string Expand(string content, string charName, string realmName)
    {
        ArgumentNullException.ThrowIfNull(content);

        return content
            .Replace(
                $"{CharToken} - {RealmToken}",
                $"{charName} - {realmName}",
                StringComparison.Ordinal
            )
            .Replace(RealmToken, realmName, StringComparison.Ordinal)
            .Replace(CharToken, charName, StringComparison.Ordinal);
    }
}

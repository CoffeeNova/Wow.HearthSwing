namespace HearthSwing.Services;

/// <summary>
/// Depersonalizes and re-personalizes WoW file content by swapping a character name and realm
/// name with stable tokens (<c>{{CHAR}}</c> / <c>{{REALM}}</c>).
/// </summary>
public interface ITemplateTokenizer
{
    /// <summary>
    /// Replaces the source character/realm names in <paramref name="content"/> with tokens.
    /// </summary>
    string Tokenize(string content, string charName, string realmName);

    /// <summary>
    /// Replaces the tokens in <paramref name="content"/> with the target character/realm names.
    /// </summary>
    string Expand(string content, string charName, string realmName);
}

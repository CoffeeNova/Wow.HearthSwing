namespace HearthSwing.Services;

/// <summary>
/// Decides whether a file inside a template should be tokenized (text) or copied byte-for-byte.
/// </summary>
public interface ITemplateFileClassifier
{
    /// <summary>
    /// Returns true when the file at the given relative path should have its content tokenized.
    /// </summary>
    bool ShouldTokenize(string relativePath);
}

using HearthSwing.Services;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class TemplateTokenizerTests
{
    private TemplateTokenizer _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sut = new TemplateTokenizer();
    }

    [Test]
    public void Tokenize_ReplacesStandaloneCharAndRealm()
    {
        // Arrange
        const string content = "player=\"Thrall\" realm=\"Firemaw\"";

        // Act
        var result = _sut.Tokenize(content, "Thrall", "Firemaw");

        // Assert
        result.ShouldBe("player=\"{{CHAR}}\" realm=\"{{REALM}}\"");
    }

    [Test]
    public void Tokenize_ReplacesCompositeKeyWithCompositeTokens()
    {
        // Arrange
        const string content = "[\"Thrall - Firemaw\"] = {}";

        // Act
        var result = _sut.Tokenize(content, "Thrall", "Firemaw");

        // Assert
        result.ShouldBe("[\"{{CHAR}} - {{REALM}}\"] = {}");
    }

    [Test]
    public void Expand_ReplacesTokensWithTargetNames()
    {
        // Arrange
        const string content = "player=\"{{CHAR}}\" realm=\"{{REALM}}\"";

        // Act
        var result = _sut.Expand(content, "Jaina", "Gehennas");

        // Assert
        result.ShouldBe("player=\"Jaina\" realm=\"Gehennas\"");
    }

    [Test]
    public void Expand_ReplacesCompositeTokenWithTargetComposite()
    {
        // Arrange
        const string content = "[\"{{CHAR}} - {{REALM}}\"] = {}";

        // Act
        var result = _sut.Expand(content, "Jaina", "Gehennas");

        // Assert
        result.ShouldBe("[\"Jaina - Gehennas\"] = {}");
    }

    [Test]
    public void TokenizeThenExpand_RoundTripsToTargetIdentity()
    {
        // Arrange
        const string content = "[\"Thrall - Firemaw\"] name=\"Thrall\" server=\"Firemaw\"";

        // Act
        var tokenized = _sut.Tokenize(content, "Thrall", "Firemaw");
        var expanded = _sut.Expand(tokenized, "Thrall", "Firemaw");

        // Assert
        expanded.ShouldBe(content);
    }

    [Test]
    public void Tokenize_WithRealmContainingSpace_ReplacesRealm()
    {
        // Arrange
        const string content = "realm=\"Blaumeux Server\" char=\"Thrall\"";

        // Act
        var result = _sut.Tokenize(content, "Thrall", "Blaumeux Server");

        // Assert
        result.ShouldBe("realm=\"{{REALM}}\" char=\"{{CHAR}}\"");
    }

    [Test]
    public void Tokenize_IsIdempotent()
    {
        // Arrange
        const string content = "name=\"Thrall\" realm=\"Firemaw\"";

        // Act
        var once = _sut.Tokenize(content, "Thrall", "Firemaw");
        var twice = _sut.Tokenize(once, "Thrall", "Firemaw");

        // Assert
        twice.ShouldBe(once);
    }

    [Test]
    public void Tokenize_WhenNamesAbsent_ReturnsContentUnchanged()
    {
        // Arrange
        const string content = "nothing to replace here";

        // Act
        var result = _sut.Tokenize(content, "Thrall", "Firemaw");

        // Assert
        result.ShouldBe(content);
    }

    [Test]
    public void Expand_WhenTokensAbsent_ReturnsContentUnchanged()
    {
        // Arrange
        const string content = "plain content without tokens";

        // Act
        var result = _sut.Expand(content, "Jaina", "Gehennas");

        // Assert
        result.ShouldBe(content);
    }

    [Test]
    public void Tokenize_WithEmptyNames_ReturnsContentUnchanged()
    {
        // Arrange
        const string content = "name=\"Thrall\"";

        // Act
        var result = _sut.Tokenize(content, string.Empty, string.Empty);

        // Assert
        result.ShouldBe(content);
    }
}

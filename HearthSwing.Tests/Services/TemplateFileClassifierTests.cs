using HearthSwing.Services;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class TemplateFileClassifierTests
{
    private TemplateFileClassifier _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sut = new TemplateFileClassifier();
    }

    [TestCase(@"SavedVariables\SomeAddon.lua")]
    [TestCase(@"SavedVariables\Deep\Nested.LUA")]
    [TestCase("macros-cache.txt")]
    [TestCase("bindings-cache.wtf")]
    [TestCase("config-cache.wtf")]
    [TestCase("chat-cache.txt")]
    [TestCase("chat-frontend-cache.txt")]
    [TestCase("edit-mode-cache-account.txt")]
    [TestCase("edit-mode-cache-character.txt")]
    [TestCase("tts-cache-account.txt")]
    [TestCase("tts-cache-character.txt")]
    [TestCase("flagged-cache-account.txt")]
    [TestCase("layout-local.txt")]
    public void ShouldTokenize_ForLuaAndAllowlistedCacheFiles_ReturnsTrue(string relativePath)
    {
        // Act
        var result = _sut.ShouldTokenize(relativePath);

        // Assert
        result.ShouldBeTrue();
    }

    [TestCase("cache.md5")]
    [TestCase(@"realm\char\cache.md5")]
    [TestCase("screenshot.tga")]
    [TestCase("unknown-file.dat")]
    [TestCase("config.wtf")]
    public void ShouldTokenize_ForBinaryOrUnknownFiles_ReturnsFalse(string relativePath)
    {
        // Act
        var result = _sut.ShouldTokenize(relativePath);

        // Assert
        result.ShouldBeFalse();
    }

    [Test]
    public void ShouldTokenize_MatchesFileNameRegardlessOfDirectory()
    {
        // Act
        var result = _sut.ShouldTokenize(@"Account\SavedVariables\macros-cache.txt");

        // Assert
        result.ShouldBeTrue();
    }

    [TestCase("")]
    [TestCase("   ")]
    public void ShouldTokenize_ForEmptyPath_ReturnsFalse(string relativePath)
    {
        // Act
        var result = _sut.ShouldTokenize(relativePath);

        // Assert
        result.ShouldBeFalse();
    }
}

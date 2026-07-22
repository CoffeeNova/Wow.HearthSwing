using System.IO;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Services;
using NSubstitute;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class DirectoryReplacerTests
{
    private IFixture _fixture = null!;
    private IFileSystem _fs = null!;
    private CapturingLogger<DirectoryReplacer> _logger = null!;
    private DirectoryReplacer _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _fs = _fixture.Freeze<IFileSystem>();

        _fs.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fs.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>()).Returns([]);
        _fs.GetDirectories(Arg.Any<string>()).Returns([]);

        _logger = new CapturingLogger<DirectoryReplacer>();
        _sut = new DirectoryReplacer(_fs, _logger);
    }

    [Test]
    public void ReplaceDirectory_WhenDestinationMissing_CopiesSourceWithoutBackup()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\dest").Returns(false);
        _fs.GetFiles(@"C:\src", "*", SearchOption.TopDirectoryOnly).Returns([@"C:\src\a.txt"]);

        // Act
        _sut.ReplaceDirectory(@"C:\src", @"C:\dest");

        // Assert
        _fs.Received().CreateDirectory(@"C:\dest");
        _fs.Received().CopyFile(@"C:\src\a.txt", @"C:\dest\a.txt");
        _fs.DidNotReceive().DeleteDirectory(@"C:\dest", Arg.Any<bool>());
    }

    [Test]
    public void ReplaceDirectory_WhenDestinationExists_BacksUpDeletesAndCopies()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\dest").Returns(true);
        _fs.GetFiles(@"C:\dest", "*", SearchOption.TopDirectoryOnly).Returns([@"C:\dest\old.txt"]);
        _fs.GetFiles(@"C:\src", "*", SearchOption.TopDirectoryOnly).Returns([@"C:\src\new.txt"]);
        _fs.DirectoryExists(Arg.Is<string>(s => s.Contains(".rollback-"))).Returns(true);

        // Act
        _sut.ReplaceDirectory(@"C:\src", @"C:\dest");

        // Assert
        _fs.Received().CopyFile(@"C:\dest\old.txt", Arg.Is<string>(s => s.Contains(".rollback-")));
        _fs.Received().DeleteDirectory(@"C:\dest", true);
        _fs.Received().CopyFile(@"C:\src\new.txt", @"C:\dest\new.txt");
    }

    [Test]
    public void ReplaceDirectory_WhenCopyFails_RestoresFromRollbackAndRethrows()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\dest").Returns(true);
        _fs.GetFiles(@"C:\dest", "*", SearchOption.TopDirectoryOnly).Returns([@"C:\dest\old.txt"]);
        _fs.GetFiles(@"C:\src", "*", SearchOption.TopDirectoryOnly).Returns([@"C:\src\new.txt"]);
        _fs.GetFiles(
                Arg.Is<string>(s => s.Contains(".rollback-")),
                "*",
                SearchOption.TopDirectoryOnly
            )
            .Returns([@"C:\rollback\old.txt"]);
        _fs.DirectoryExists(Arg.Is<string>(s => s.Contains(".rollback-"))).Returns(true);
        _fs.When(fs => fs.CopyFile(@"C:\src\new.txt", @"C:\dest\new.txt"))
            .Do(_ => throw new IOException("copy failed"));

        // Act
        var act = () => _sut.ReplaceDirectory(@"C:\src", @"C:\dest");

        // Assert
        act.ShouldThrow<IOException>();
        _fs.Received().CopyFile(@"C:\rollback\old.txt", @"C:\dest\old.txt");
        _logger
            .HasLog(
                Microsoft.Extensions.Logging.LogLevel.Error,
                m => m.Contains("Failed to replace")
            )
            .ShouldBeTrue();
    }

    [Test]
    public void ClearReadOnlyAttributes_ClearsReadOnlyFilesOnly()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\dir").Returns(true);
        _fs.GetFiles(@"C:\dir", "*", SearchOption.AllDirectories)
            .Returns([@"C:\dir\ro.txt", @"C:\dir\rw.txt"]);
        _fs.GetAttributes(@"C:\dir\ro.txt").Returns(FileAttributes.ReadOnly);
        _fs.GetAttributes(@"C:\dir\rw.txt").Returns(FileAttributes.Normal);

        // Act
        _sut.ClearReadOnlyAttributes(@"C:\dir");

        // Assert
        _fs.Received()
            .SetAttributes(@"C:\dir\ro.txt", FileAttributes.ReadOnly & ~FileAttributes.ReadOnly);
        _fs.DidNotReceive().SetAttributes(@"C:\dir\rw.txt", Arg.Any<FileAttributes>());
    }
}

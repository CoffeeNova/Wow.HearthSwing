using System.Text.Json;
using System.Text.Json.Serialization;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models;
using HearthSwing.Models.WoW;
using HearthSwing.Services;
using NSubstitute;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class ChangeHistoryServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private IFixture _fixture = null!;
    private IArchiveService _archive = null!;
    private IFileSystem _fileSystem = null!;
    private IDirectoryReplacer _directoryReplacer = null!;
    private IWtfInspector _wtfInspector = null!;
    private ISettingsService _settingsService = null!;
    private ChangeHistoryService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _archive = _fixture.Freeze<IArchiveService>();
        _fileSystem = _fixture.Freeze<IFileSystem>();
        _directoryReplacer = _fixture.Freeze<IDirectoryReplacer>();
        _wtfInspector = _fixture.Freeze<IWtfInspector>();
        _settingsService = _fixture.Freeze<ISettingsService>();

        _archive
            .CompressDirectoryAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);
        _archive
            .ExtractToDirectoryAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        _fileSystem.GetDirectories(Arg.Any<string>()).Returns([]);
        _fileSystem
            .GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
            .Returns([]);
        _fileSystem.GetFileLength(Arg.Any<string>()).Returns(1024L);

        _settingsService.Current.Returns(
            new AppSettings
            {
                GamePath = @"C:\Game",
                ProfilesPath = @"C:\Profiles",
                MaxHistoryEntriesPerTarget = 20,
            }
        );

        _sut = new ChangeHistoryService(
            _archive,
            _fileSystem,
            _directoryReplacer,
            _wtfInspector,
            _settingsService
        );
    }

    [Test]
    public async Task SnapshotAsync_WhenSourceExists_CreatesArchiveAndIndex()
    {
        // Arrange
        const string sourceFolder = @"C:\Game\WTF\Account\Alt\Realm\Jaina";
        _fileSystem.DirectoryExists(sourceFolder).Returns(true);
        _fileSystem.FileExists(Arg.Is<string>(path => path.EndsWith(".tar.gz"))).Returns(true);

        string? writtenIndex = null;
        _fileSystem
            .When(fileSystem =>
                fileSystem.WriteAllText(
                    Arg.Is<string>(path => path.EndsWith("index.json")),
                    Arg.Any<string>()
                )
            )
            .Do(callInfo => writtenIndex = callInfo.ArgAt<string>(1));

        // Act
        var entry = await _sut.SnapshotAsync(
            "wtf/char/Alt/Realm/Jaina",
            HistoryTargetKind.WtfCharacter,
            sourceFolder,
            "Applied template Mage"
        );

        // Assert
        _fileSystem.Received().CreateDirectory(Arg.Is<string>(path => path.Contains(@".history")));
        _ = _archive
            .Received()
            .CompressDirectoryAsync(
                sourceFolder,
                Arg.Is<string>(path => path.Contains(@".history") && path.EndsWith(".tar.gz")),
                Arg.Any<CancellationToken>()
            );

        writtenIndex.ShouldNotBeNullOrWhiteSpace();
        var entries = JsonSerializer.Deserialize<List<HistoryEntry>>(writtenIndex, JsonOptions);
        entries.ShouldNotBeNull();
        entries.Count.ShouldBe(1);
        entries[0].TargetKey.ShouldBe("wtf/char/Alt/Realm/Jaina");
        entries[0].AccountName.ShouldBe("Alt");
        entries[0].RealmName.ShouldBe("Realm");
        entries[0].CharacterName.ShouldBe("Jaina");

        entry.SizeBytes.ShouldBe(1024L);
    }

    [Test]
    public async Task SnapshotAsync_WhenConfiguredLimitIsInvalid_UsesDefaultLimitOf20()
    {
        // Arrange
        _settingsService.Current.Returns(
            new AppSettings
            {
                GamePath = @"C:\Game",
                ProfilesPath = @"C:\Profiles",
                MaxHistoryEntriesPerTarget = 0,
            }
        );

        const string sourceFolder = @"C:\Game\WTF\Account\Alt\Realm\Jaina";
        const string historyFolder = @"C:\Profiles\.history\wtf\char\Alt\Realm\Jaina";
        const string indexPath = historyFolder + @"\index.json";

        _fileSystem.DirectoryExists(sourceFolder).Returns(true);
        _fileSystem.FileExists(indexPath).Returns(true);
        _fileSystem.FileExists(Arg.Is<string>(path => path.EndsWith(".tar.gz"))).Returns(true);

        var existing = Enumerable
            .Range(1, 20)
            .Select(day => new HistoryEntry
            {
                TargetKey = "wtf/char/Alt/Realm/Jaina",
                Kind = HistoryTargetKind.WtfCharacter,
                CreatedUtc = new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero),
                Description = "Existing",
                ArchivePath =
                    $@"C:\Profiles\.history\wtf\char\Alt\Realm\Jaina\existing-{day}.tar.gz",
                SizeBytes = 100,
                AccountName = "Alt",
                RealmName = "Realm",
                CharacterName = "Jaina",
            })
            .ToList();

        _fileSystem.ReadAllText(indexPath).Returns(JsonSerializer.Serialize(existing, JsonOptions));

        // Act
        await _sut.SnapshotAsync(
            "wtf/char/Alt/Realm/Jaina",
            HistoryTargetKind.WtfCharacter,
            sourceFolder,
            "Applied template"
        );

        // Assert
        _fileSystem.Received().DeleteFile(existing[0].ArchivePath);
        _fileSystem.DidNotReceive().DeleteFile(existing[^1].ArchivePath);
    }

    [Test]
    public async Task SnapshotAsync_WhenConfiguredLimitIsProvided_TrimsToConfiguredLimit()
    {
        // Arrange
        _settingsService.Current.Returns(
            new AppSettings
            {
                GamePath = @"C:\Game",
                ProfilesPath = @"C:\Profiles",
                MaxHistoryEntriesPerTarget = 2,
            }
        );

        const string sourceFolder = @"C:\Game\WTF\Account\Alt\Realm\Jaina";
        const string historyFolder = @"C:\Profiles\.history\wtf\char\Alt\Realm\Jaina";
        const string indexPath = historyFolder + @"\index.json";

        _fileSystem.DirectoryExists(sourceFolder).Returns(true);
        _fileSystem.FileExists(indexPath).Returns(true);
        _fileSystem.FileExists(Arg.Is<string>(path => path.EndsWith(".tar.gz"))).Returns(true);

        var existing = new List<HistoryEntry>
        {
            new()
            {
                TargetKey = "wtf/char/Alt/Realm/Jaina",
                Kind = HistoryTargetKind.WtfCharacter,
                CreatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Description = "Old",
                ArchivePath = @"C:\Profiles\.history\wtf\char\Alt\Realm\Jaina\old.tar.gz",
                SizeBytes = 100,
                AccountName = "Alt",
                RealmName = "Realm",
                CharacterName = "Jaina",
            },
            new()
            {
                TargetKey = "wtf/char/Alt/Realm/Jaina",
                Kind = HistoryTargetKind.WtfCharacter,
                CreatedUtc = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                Description = "Newer",
                ArchivePath = @"C:\Profiles\.history\wtf\char\Alt\Realm\Jaina\newer.tar.gz",
                SizeBytes = 100,
                AccountName = "Alt",
                RealmName = "Realm",
                CharacterName = "Jaina",
            },
        };

        _fileSystem.ReadAllText(indexPath).Returns(JsonSerializer.Serialize(existing, JsonOptions));

        // Act
        await _sut.SnapshotAsync(
            "wtf/char/Alt/Realm/Jaina",
            HistoryTargetKind.WtfCharacter,
            sourceFolder,
            "Applied template"
        );

        // Assert
        _fileSystem
            .Received()
            .DeleteFile(@"C:\Profiles\.history\wtf\char\Alt\Realm\Jaina\old.tar.gz");
        _fileSystem
            .DidNotReceive()
            .DeleteFile(@"C:\Profiles\.history\wtf\char\Alt\Realm\Jaina\newer.tar.gz");
    }

    [Test]
    public async Task RestoreAsync_ForCharacterTarget_SnapshotsCurrentAndReplacesResolvedFolder()
    {
        // Arrange
        var entry = new HistoryEntry
        {
            TargetKey = "wtf/char/Alt/Realm/Jaina",
            Kind = HistoryTargetKind.WtfCharacter,
            CreatedUtc = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero),
            Description = "Applied template",
            ArchivePath = @"C:\Profiles\.history\wtf\char\Alt\Realm\Jaina\entry.tar.gz",
            SizeBytes = 100,
            AccountName = "Alt",
            RealmName = "Realm",
            CharacterName = "Jaina",
        };

        const string targetPath = @"C:\Game\WTF\Account\Alt\Realm\Jaina";
        var installation = new WowInstallation
        {
            GamePath = @"C:\Game",
            WtfPath = @"C:\Game\WTF",
            Accounts =
            [
                new WowAccount
                {
                    AccountName = "Alt",
                    FolderPath = @"C:\Game\WTF\Account\Alt",
                    Realms =
                    [
                        new WowRealm
                        {
                            AccountName = "Alt",
                            RealmName = "Realm",
                            FolderPath = @"C:\Game\WTF\Account\Alt\Realm",
                            Characters =
                            [
                                new WowCharacter
                                {
                                    AccountName = "Alt",
                                    RealmName = "Realm",
                                    CharacterName = "Jaina",
                                    FolderPath = targetPath,
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        _wtfInspector.Inspect(@"C:\Game").Returns(installation);
        _fileSystem.FileExists(entry.ArchivePath).Returns(true);
        _fileSystem.DirectoryExists(targetPath).Returns(true);
        _fileSystem.FileExists(Arg.Is<string>(path => path.EndsWith(".tar.gz"))).Returns(true);

        // Act
        await _sut.RestoreAsync(entry);

        // Assert
        Received.InOrder(() =>
        {
            _archive.ExtractToDirectoryAsync(
                entry.ArchivePath,
                Arg.Is<string>(path => path.Contains(".restore-")),
                Arg.Any<CancellationToken>()
            );
            _archive.CompressDirectoryAsync(
                targetPath,
                Arg.Is<string>(path => path.Contains(@".history") && path.EndsWith(".tar.gz")),
                Arg.Any<CancellationToken>()
            );
            _directoryReplacer.ReplaceDirectory(
                Arg.Is<string>(path => path.Contains(".restore-")),
                targetPath
            );
        });
    }

    [Test]
    public async Task RestoreAsync_ForAccountTarget_UsesResolvedAccountFolder()
    {
        // Arrange
        var entry = new HistoryEntry
        {
            TargetKey = "wtf/account/Alt",
            Kind = HistoryTargetKind.WtfAccount,
            CreatedUtc = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero),
            Description = "Applied account template",
            ArchivePath = @"C:\Profiles\.history\wtf\account\Alt\entry.tar.gz",
            SizeBytes = 100,
            AccountName = "Alt",
        };

        const string accountFolderPath = @"C:\Game\WTF\Account\Alt";
        _wtfInspector
            .Inspect(@"C:\Game")
            .Returns(
                new WowInstallation
                {
                    GamePath = @"C:\Game",
                    WtfPath = @"C:\Game\WTF",
                    Accounts =
                    [
                        new WowAccount
                        {
                            AccountName = "Alt",
                            FolderPath = accountFolderPath,
                            Realms = [],
                        },
                    ],
                }
            );

        _fileSystem.FileExists(entry.ArchivePath).Returns(true);
        _fileSystem.DirectoryExists(accountFolderPath).Returns(true);
        _fileSystem.FileExists(Arg.Is<string>(path => path.EndsWith(".tar.gz"))).Returns(true);
        _fileSystem
            .DirectoryExists(
                Arg.Is<string>(path =>
                    path.Contains(".restore-") && path.EndsWith(@"\SavedVariables")
                )
            )
            .Returns(true);

        // Act
        await _sut.RestoreAsync(entry);

        // Assert
        _directoryReplacer
            .Received()
            .ReplaceDirectory(
                Arg.Is<string>(path =>
                    path.Contains(".restore-") && path.EndsWith(@"\SavedVariables")
                ),
                @"C:\Game\WTF\Account\Alt\SavedVariables"
            );
        _directoryReplacer
            .DidNotReceive()
            .ReplaceDirectory(
                Arg.Is<string>(path => path.Contains(".restore-")),
                accountFolderPath
            );
    }
}

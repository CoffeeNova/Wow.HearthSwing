using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models;
using HearthSwing.Models.Templates;
using HearthSwing.Models.WoW;
using HearthSwing.Services;
using NSubstitute;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class TemplateRestoreOrchestratorTests
{
    private IFixture _fixture = null!;
    private ICacheProtector _cacheProtector = null!;
    private IChangeHistoryService _changeHistoryService = null!;
    private ISettingsService _settingsService = null!;
    private IProcessMonitor _processMonitor = null!;
    private ITemplateApplyService _templateApplyService = null!;
    private TemplateRestoreOrchestrator _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _cacheProtector = _fixture.Freeze<ICacheProtector>();
        _changeHistoryService = _fixture.Freeze<IChangeHistoryService>();
        _settingsService = _fixture.Freeze<ISettingsService>();
        _processMonitor = _fixture.Freeze<IProcessMonitor>();
        _templateApplyService = _fixture.Freeze<ITemplateApplyService>();

        _settingsService.Current.Returns(new AppSettings { GamePath = @"C:\Game" });
        _processMonitor.IsWowRunning().Returns(false);
        _changeHistoryService
            .SnapshotAsync(
                Arg.Any<string>(),
                Arg.Any<HistoryTargetKind>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
                Task.FromResult(
                    new HistoryEntry
                    {
                        TargetKey = "wtf/char/demo/demo/demo",
                        Kind = HistoryTargetKind.WtfCharacter,
                        CreatedUtc = DateTimeOffset.UtcNow,
                        Description = "snapshot",
                        ArchivePath = @"C:\Profiles\.history\demo.tar.gz",
                    }
                )
            );

        _sut = new TemplateRestoreOrchestrator(
            _cacheProtector,
            _changeHistoryService,
            _settingsService,
            _processMonitor,
            _templateApplyService,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<TemplateRestoreOrchestrator>>()
        );
    }

    [TearDown]
    public void TearDown()
    {
        _cacheProtector.Dispose();
    }

    [Test]
    public async Task RestoreCharacterTemplate_WhenWowClosed_CreatesHistorySnapshotsAndApplies()
    {
        // Arrange
        var template = new TemplateSummary
        {
            Id = "Warlock",
            Name = "Warlock - TBC",
            Kind = TemplateKind.Character,
            RootPath = @"C:\Profiles\.templates\Warlock",
            SourceAccountName = "Main",
        };
        var target = new WowCharacter
        {
            AccountName = "Alt",
            RealmName = "Realm",
            CharacterName = "Jaina",
            FolderPath = @"C:\Game\WTF\Account\Alt\Realm\Jaina",
        };

        // Act
        await _sut.RestoreCharacterTemplateAsync(template, target, new TemplateRestoreOptions());

        // Assert
        _ = _changeHistoryService
            .Received(1)
            .SnapshotAsync(
                "wtf/char/Alt/Realm/Jaina",
                HistoryTargetKind.WtfCharacter,
                @"C:\Game\WTF\Account\Alt\Realm\Jaina",
                Arg.Is<string>(text => text.Contains("Applied template")),
                Arg.Any<CancellationToken>()
            );
        _ = _changeHistoryService
            .Received(1)
            .SnapshotAsync(
                "wtf/account/Alt",
                HistoryTargetKind.WtfAccount,
                @"C:\Game\WTF\Account\Alt",
                Arg.Is<string>(text => text.Contains("Applied template")),
                Arg.Any<CancellationToken>()
            );
        _templateApplyService
            .Received()
            .ApplyCharacterTemplate(template, target, TemplateApplyScope.Full, true, true);
        _cacheProtector.DidNotReceive().Unlock();
        _cacheProtector.DidNotReceive().ForceRestore(Arg.Any<string>());
    }

    [Test]
    public async Task RestoreCharacterTemplate_WhenIncludeAccountScopedDisabled_PassesFlagToApplyService()
    {
        // Arrange
        var template = new TemplateSummary
        {
            Id = "Warlock",
            Name = "Warlock - TBC",
            Kind = TemplateKind.Character,
            RootPath = @"C:\Profiles\.templates\Warlock",
            SourceAccountName = "Main",
        };
        var target = new WowCharacter
        {
            AccountName = "Alt",
            RealmName = "Realm",
            CharacterName = "Jaina",
            FolderPath = @"C:\Game\WTF\Account\Alt\Realm\Jaina",
        };

        // Act
        await _sut.RestoreCharacterTemplateAsync(
            template,
            target,
            new TemplateRestoreOptions { IncludeAccountScoped = false }
        );

        // Assert
        _ = _changeHistoryService
            .Received(1)
            .SnapshotAsync(
                "wtf/char/Alt/Realm/Jaina",
                HistoryTargetKind.WtfCharacter,
                @"C:\Game\WTF\Account\Alt\Realm\Jaina",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        _ = _changeHistoryService
            .DidNotReceive()
            .SnapshotAsync(
                "wtf/account/Alt",
                HistoryTargetKind.WtfAccount,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        _templateApplyService
            .Received()
            .ApplyCharacterTemplate(template, target, TemplateApplyScope.Full, false, true);
    }

    [Test]
    public async Task RestoreAccountTemplate_WhenWowRunning_UnlocksAppliesRelocksAndForceRestores()
    {
        // Arrange
        _processMonitor.IsWowRunning().Returns(true);
        var template = new TemplateSummary
        {
            Id = "Warlock",
            Name = "Warlock - Account",
            Kind = TemplateKind.Account,
            RootPath = @"C:\Profiles\.templates\Warlock",
            SourceAccountName = "Main",
        };
        var target = new WowAccount
        {
            AccountName = "Alt",
            FolderPath = @"C:\Game\WTF\Account\Alt",
        };
        var messages = new List<string>();
        _sut.Log += messages.Add;

        // Act
        await _sut.RestoreAccountTemplateAsync(
            template,
            target,
            new TemplateRestoreOptions { Scope = TemplateApplyScope.CacheOnly }
        );

        // Assert
        _ = _changeHistoryService
            .Received(1)
            .SnapshotAsync(
                "wtf/account/Alt",
                HistoryTargetKind.WtfAccount,
                @"C:\Game\WTF\Account\Alt",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        Received.InOrder(() =>
        {
            _cacheProtector.Unlock();
            _templateApplyService.ApplyAccountTemplate(
                template,
                target,
                TemplateApplyScope.CacheOnly,
                false
            );
            _cacheProtector.Lock(@"C:\Game\WTF", "Alt");
            _cacheProtector.ForceRestore(@"C:\Game\WTF");
        });
        messages.ShouldHaveSingleItem();
        messages[0].ShouldContain("/reload");
    }

    [Test]
    public async Task RestoreCharacterTemplate_WhenWowRunningAndApplyFails_RelocksAndRethrows()
    {
        // Arrange
        _processMonitor.IsWowRunning().Returns(true);
        var template = new TemplateSummary
        {
            Id = "Warlock",
            Name = "Warlock - TBC",
            Kind = TemplateKind.Character,
            RootPath = @"C:\Profiles\.templates\Warlock",
            SourceAccountName = "Main",
        };
        var target = new WowCharacter
        {
            AccountName = "Alt",
            RealmName = "Realm",
            CharacterName = "Jaina",
            FolderPath = @"C:\Game\WTF\Account\Alt\Realm\Jaina",
        };

        var expected = new InvalidOperationException("apply failed");
        _templateApplyService
            .When(service =>
                service.ApplyCharacterTemplate(
                    template,
                    target,
                    TemplateApplyScope.Full,
                    true,
                    false
                )
            )
            .Do(_ => throw expected);

        // Act
        var action = async () =>
            await _sut.RestoreCharacterTemplateAsync(
                template,
                target,
                new TemplateRestoreOptions()
            );

        // Assert
        await action.ShouldThrowAsync<InvalidOperationException>();
        Received.InOrder(() =>
        {
            _cacheProtector.Unlock();
            _cacheProtector.Lock(@"C:\Game\WTF", "Alt");
            _cacheProtector.ForceRestore(@"C:\Game\WTF");
        });
    }

    [Test]
    public async Task RestoreCharacterTemplate_AlwaysCreatesHistorySnapshot()
    {
        // Arrange
        var template = new TemplateSummary
        {
            Id = "Warlock",
            Name = "Warlock - TBC",
            Kind = TemplateKind.Character,
            RootPath = @"C:\Profiles\.templates\Warlock",
            SourceAccountName = "Main",
        };
        var target = new WowCharacter
        {
            AccountName = "Alt",
            RealmName = "Realm",
            CharacterName = "Jaina",
            FolderPath = @"C:\Game\WTF\Account\Alt\Realm\Jaina",
        };

        // Act
        await _sut.RestoreCharacterTemplateAsync(template, target, new TemplateRestoreOptions());

        // Assert
        _ = _changeHistoryService
            .Received(1)
            .SnapshotAsync(
                "wtf/char/Alt/Realm/Jaina",
                HistoryTargetKind.WtfCharacter,
                @"C:\Game\WTF\Account\Alt\Realm\Jaina",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        _templateApplyService
            .Received()
            .ApplyCharacterTemplate(template, target, TemplateApplyScope.Full, true, true);
    }
}

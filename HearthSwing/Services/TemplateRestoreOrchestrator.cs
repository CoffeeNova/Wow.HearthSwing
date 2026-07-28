using System.IO;
using HearthSwing.Models;
using HearthSwing.Models.Templates;
using HearthSwing.Models.WoW;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

public sealed class TemplateRestoreOrchestrator : ITemplateRestoreOrchestrator
{
    private const string WtfFolderName = "WTF";

    private readonly ICacheProtector _cacheProtector;
    private readonly IChangeHistoryService _changeHistoryService;
    private readonly ISettingsService _settingsService;
    private readonly IProcessMonitor _processMonitor;
    private readonly ITemplateApplyService _templateApplyService;
    private readonly ILogger<TemplateRestoreOrchestrator> _logger;

    public TemplateRestoreOrchestrator(
        ICacheProtector cacheProtector,
        IChangeHistoryService changeHistoryService,
        ISettingsService settingsService,
        IProcessMonitor processMonitor,
        ITemplateApplyService templateApplyService,
        ILogger<TemplateRestoreOrchestrator> logger
    )
    {
        _cacheProtector = cacheProtector;
        _changeHistoryService = changeHistoryService;
        _settingsService = settingsService;
        _processMonitor = processMonitor;
        _templateApplyService = templateApplyService;
        _logger = logger;
    }

    public event Action<string>? Log;

    public async Task RestoreCharacterTemplateAsync(
        TemplateSummary template,
        WowCharacter target,
        TemplateRestoreOptions options,
        CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        var wowRunning = _processMonitor.IsWowRunning();

        await RestoreAsync(
            template: template,
            applyTemplate: () =>
                _templateApplyService.ApplyCharacterTemplate(
                    template,
                    target,
                    options.Scope,
                    options.IncludeAccountScoped,
                    useDirectorySwap: !wowRunning
                ),
            characterTarget: target,
            accountTarget: null,
            wowRunning: wowRunning,
            options: options,
            ct: ct
        );
    }

    public async Task RestoreAccountTemplateAsync(
        TemplateSummary template,
        WowAccount target,
        TemplateRestoreOptions options,
        CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        var wowRunning = _processMonitor.IsWowRunning();

        await RestoreAsync(
            template: template,
            applyTemplate: () =>
                _templateApplyService.ApplyAccountTemplate(
                    template,
                    target,
                    options.Scope,
                    useDirectorySwap: !wowRunning
                ),
            characterTarget: null,
            accountTarget: target,
            wowRunning: wowRunning,
            options: options,
            ct: ct
        );
    }

    private async Task RestoreAsync(
        TemplateSummary template,
        Action applyTemplate,
        WowCharacter? characterTarget,
        WowAccount? accountTarget,
        bool wowRunning,
        TemplateRestoreOptions options,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        var accountName = characterTarget?.AccountName ?? accountTarget?.AccountName;
        if (string.IsNullOrWhiteSpace(accountName))
            throw new InvalidOperationException("A restore target account name was not provided.");

        await CreateTargetHistorySnapshotsAsync(
            template,
            characterTarget,
            accountTarget,
            options,
            ct
        );

        if (wowRunning)
        {
            var wtfPath = GetWtfPath();
            _cacheProtector.Unlock();
            try
            {
                await RunApplyAsync(applyTemplate, ct);
            }
            finally
            {
                _cacheProtector.Lock(wtfPath, accountName);
                _cacheProtector.ForceRestore(wtfPath);
            }

            RaiseLog("Ready - enter /reload in WoW.");
            _logger.LogInformation(
                "Restored template '{TemplateId}' while WoW was running.",
                template.Id
            );
            return;
        }

        await RunApplyAsync(applyTemplate, ct);
        _logger.LogInformation(
            "Restored template '{TemplateId}' while WoW was closed.",
            template.Id
        );
    }

    private async Task CreateTargetHistorySnapshotsAsync(
        TemplateSummary template,
        WowCharacter? characterTarget,
        WowAccount? accountTarget,
        TemplateRestoreOptions options,
        CancellationToken ct
    )
    {
        if (characterTarget is not null)
        {
            await SnapshotCharacterTargetAsync(template, characterTarget, ct);
            if (options.IncludeAccountScoped)
            {
                await SnapshotAccountTargetAsync(
                    template,
                    characterTarget.AccountName,
                    GetAccountRoot(characterTarget),
                    ct
                );
            }

            return;
        }

        if (accountTarget is not null)
        {
            await SnapshotAccountTargetAsync(
                template,
                accountTarget.AccountName,
                accountTarget.FolderPath,
                ct
            );
            return;
        }

        throw new InvalidOperationException("A restore target was not provided.");
    }

    private Task SnapshotCharacterTargetAsync(
        TemplateSummary template,
        WowCharacter target,
        CancellationToken ct
    )
    {
        return _changeHistoryService.SnapshotAsync(
            BuildCharacterTargetKey(target),
            HistoryTargetKind.WtfCharacter,
            target.FolderPath,
            $"Applied template '{template.Name}'",
            ct
        );
    }

    private Task SnapshotAccountTargetAsync(
        TemplateSummary template,
        string accountName,
        string accountPath,
        CancellationToken ct
    )
    {
        return _changeHistoryService.SnapshotAsync(
            BuildAccountTargetKey(accountName),
            HistoryTargetKind.WtfAccount,
            accountPath,
            $"Applied template '{template.Name}'",
            ct
        );
    }

    private static string GetAccountRoot(WowCharacter target)
    {
        var realmPath = Path.GetDirectoryName(target.FolderPath);
        var accountPath = realmPath is null ? null : Path.GetDirectoryName(realmPath);

        return !string.IsNullOrWhiteSpace(accountPath)
            ? accountPath
            : throw new InvalidOperationException(
                $"Could not resolve account folder for '{target.FolderPath}'."
            );
    }

    private static string BuildCharacterTargetKey(WowCharacter target)
    {
        return string.Join(
            '/',
            "wtf",
            "char",
            target.AccountName,
            target.RealmName,
            target.CharacterName
        );
    }

    private static string BuildAccountTargetKey(string accountName)
    {
        return string.Join('/', "wtf", "account", accountName);
    }

    private string GetWtfPath()
    {
        if (string.IsNullOrWhiteSpace(_settingsService.Current.GamePath))
            throw new InvalidOperationException("Game path is not configured.");

        return Path.Combine(_settingsService.Current.GamePath, WtfFolderName);
    }

    private static Task RunApplyAsync(Action applyTemplate, CancellationToken ct)
    {
        return Task.Run(
            () =>
            {
                ct.ThrowIfCancellationRequested();
                applyTemplate();
            },
            ct
        );
    }

    private void RaiseLog(string message)
    {
        Log?.Invoke(message);
    }
}

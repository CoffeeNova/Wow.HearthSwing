using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using HearthSwing.Models;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

public sealed class UpdateService : IUpdateService
{
    private const string ReleaseUrl =
        "https://api.github.com/repos/CoffeeNova/Wow.HearthSwing/releases/latest";

    private static readonly HttpClient Http = CreateHttpClient();

    private readonly ILogger<UpdateService> _logger;

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
    }

    public async Task<UpdateCheckResult?> CheckForUpdateAsync(
        string currentVersion,
        CancellationToken ct
    )
    {
        using var response = await Http.GetAsync(ReleaseUrl, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );

        var root = doc.RootElement;
        var tagName = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var remoteVersion = tagName.TrimStart('v');

        if (!Version.TryParse(remoteVersion, out var remote))
            return null;

        if (!Version.TryParse(currentVersion, out var current))
            return null;

        if (remote <= current)
            return null;

        var downloadUrl = ExtractAssetUrl(root);
        if (string.IsNullOrEmpty(downloadUrl))
            return null;

        _ = root.TryGetProperty("body", out var body)
            ? body.GetString() ?? string.Empty
            : string.Empty;

        return new UpdateCheckResult { Version = remoteVersion, DownloadUrl = downloadUrl };
    }

    public async Task ApplyUpdateAsync(UpdateCheckResult update, CancellationToken ct)
    {
        var appDir = AppContext.BaseDirectory;
        var tempZip = Path.Combine(Path.GetTempPath(), $"HearthSwing-{update.Version}.zip");
        var tempExtract = Path.Combine(Path.GetTempPath(), $"HearthSwing-{update.Version}");

        try
        {
            _logger.LogInformation("Downloading HearthSwing {Version}...", update.Version);
            await DownloadFileAsync(update.DownloadUrl, tempZip, ct);
            _logger.LogInformation("Download complete. Extracting...");

            if (Directory.Exists(tempExtract))
                Directory.Delete(tempExtract, recursive: true);

            await ZipFile.ExtractToDirectoryAsync(tempZip, tempExtract, ct);

            RenameCurrentFiles(appDir);
            CopyNewFiles(tempExtract, appDir);

            _logger.LogInformation("Update applied. Restarting...");

            var exePath = Path.Combine(appDir, "HearthSwing.exe");
            Process.Start(new ProcessStartInfo { FileName = exePath, UseShellExecute = true });
            Application.Current?.Dispatcher.Invoke(() => Application.Current.Shutdown());
        }
        finally
        {
            CleanupTemp(tempZip, tempExtract);
        }
    }

    /// <summary>
    /// Deletes leftover *.old files from a previous update.
    /// Call on startup from the UI thread.
    /// </summary>
    public void CleanupPreviousUpdate()
    {
        var appDir = AppContext.BaseDirectory;
        try
        {
            foreach (var oldFile in Directory.GetFiles(appDir, "*.old"))
                File.Delete(oldFile);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clean up previous update files in {AppDirectory}.", appDir);
        }
    }

    private static string? ExtractAssetUrl(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets))
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return asset.GetProperty("browser_download_url").GetString();
        }

        return null;
    }

    private async Task DownloadFileAsync(string url, string destPath, CancellationToken ct)
    {
        await using var stream = await Http.GetStreamAsync(url, ct);
        await using var fileStream = new FileStream(
            destPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None
        );
        await stream.CopyToAsync(fileStream, ct);
    }

    private void RenameCurrentFiles(string appDir)
    {
        var exePath = Path.Combine(appDir, "HearthSwing.exe");
        if (File.Exists(exePath))
        {
            var oldPath = exePath + ".old";
            if (File.Exists(oldPath))
                File.Delete(oldPath);
            File.Move(exePath, oldPath);
            _logger.LogInformation("Renamed current exe to HearthSwing.exe.old");
        }
    }

    private static void CopyNewFiles(string sourceDir, string destDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var destFile = Path.Combine(destDir, relativePath);

            var destSubDir = Path.GetDirectoryName(destFile);
            if (destSubDir is not null && !Directory.Exists(destSubDir))
                Directory.CreateDirectory(destSubDir);

            File.Copy(file, destFile, overwrite: true);
        }
    }

    private static void CleanupTemp(string tempZip, string tempExtract)
    {
        try
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }
        catch
        { /* best effort */
        }
        try
        {
            if (Directory.Exists(tempExtract))
                Directory.Delete(tempExtract, recursive: true);
        }
        catch
        { /* best effort */
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HearthSwing");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

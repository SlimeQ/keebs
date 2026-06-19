using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Keebs;

internal sealed class GitHubReleaseUpdater
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/SlimeQ/keebs/releases/latest";
    private const string UserAgent = "Keebs-Updater";
    private readonly HttpClient _httpClient;

    public GitHubReleaseUpdater()
        : this(new HttpClient())
    {
    }

    internal GitHubReleaseUpdater(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public static Version CurrentVersion => GetCurrentVersion();

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (release is null || !TryParseVersion(release.TagName, out var latestVersion))
        {
            return UpdateCheckResult.Unavailable("Could not read the latest GitHub release.");
        }

        var currentVersion = CurrentVersion;
        var installerAsset = SelectInstallerAsset(release.Assets);
        var updateAvailable = CompareVersions(latestVersion, currentVersion) > 0;

        return new UpdateCheckResult(
            updateAvailable,
            currentVersion,
            latestVersion,
            installerAsset?.BrowserDownloadUrl,
            release.HtmlUrl,
            updateAvailable
                ? $"Keebs {latestVersion} is available."
                : $"Keebs is up to date ({currentVersion}).");
    }

    public async Task<string> DownloadInstallerAsync(
        UpdateCheckResult update,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.InstallerUrl))
        {
            throw new InvalidOperationException("The latest GitHub release does not include a Keebs MSI installer.");
        }

        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Keebs",
            "Updates");
        Directory.CreateDirectory(updateDirectory);

        var installerPath = Path.Combine(updateDirectory, $"Keebs-Setup-{update.LatestVersion}-win-x64.msi");
        using var request = new HttpRequestMessage(HttpMethod.Get, update.InstallerUrl);
        request.Headers.UserAgent.ParseAdd(UserAgent);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var remoteStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var localStream = File.Create(installerPath);
        await remoteStream.CopyToAsync(localStream, cancellationToken).ConfigureAwait(false);

        return installerPath;
    }

    public static void LaunchInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException("The downloaded installer was not found.", installerPath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            Arguments = $"/i \"{installerPath}\"",
            UseShellExecute = true,
            Verb = "runas"
        });
    }

    internal static GitHubReleaseAsset? SelectInstallerAsset(IReadOnlyList<GitHubReleaseAsset> assets)
    {
        return assets.FirstOrDefault(asset =>
                   asset.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) &&
                   asset.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase)) ??
               assets.FirstOrDefault(asset => asset.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool TryParseVersion(string value, out Version version)
    {
        var normalizedValue = value.Trim();
        if (normalizedValue.StartsWith('v') || normalizedValue.StartsWith('V'))
        {
            normalizedValue = normalizedValue[1..];
        }

        var metadataIndex = normalizedValue.IndexOfAny(['+', '-']);
        if (metadataIndex >= 0)
        {
            normalizedValue = normalizedValue[..metadataIndex];
        }

        return Version.TryParse(normalizedValue, out version!);
    }

    internal static int CompareVersions(Version left, Version right)
    {
        for (var part = 0; part < 4; part++)
        {
            var comparison = GetPart(left, part).CompareTo(GetPart(right, part));
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static Version GetCurrentVersion()
    {
        var informationalVersion = typeof(GitHubReleaseUpdater)
            .Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        return !string.IsNullOrWhiteSpace(informationalVersion) &&
               TryParseVersion(informationalVersion, out var version)
            ? version
            : typeof(GitHubReleaseUpdater).Assembly.GetName().Version ?? new Version(0, 0, 0);
    }

    private static int GetPart(Version version, int part)
    {
        return part switch
        {
            0 => version.Major,
            1 => version.Minor,
            2 => Math.Max(version.Build, 0),
            3 => Math.Max(version.Revision, 0),
            _ => 0
        };
    }
}

internal sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    Version CurrentVersion,
    Version LatestVersion,
    string? InstallerUrl,
    string? ReleaseUrl,
    string Message)
{
    public static UpdateCheckResult Unavailable(string message)
    {
        return new UpdateCheckResult(false, GitHubReleaseUpdater.CurrentVersion, GitHubReleaseUpdater.CurrentVersion, null, null, message);
    }
}

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; init; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset> Assets { get; init; } = [];
}

internal sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; init; } = string.Empty;
}

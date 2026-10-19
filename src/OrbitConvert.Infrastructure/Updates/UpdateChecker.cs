using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace OrbitConvert.Infrastructure.Updates;

public sealed record UpdateInfo(
    Version Current,
    Version Latest,
    string ReleaseUrl,
    string? InstallerUrl = null,
    string? InstallerSha256 = null)
{
    public bool IsNewer => Latest > Current;
    public bool CanInstall => IsNewer && InstallerUrl is not null && InstallerSha256 is not null;
}

/// <summary>
/// Asks GitHub for the latest release. Called only when the user enabled update checks or clicked
/// "Check now". It sends no data beyond a standard HTTPS request (no identifiers, no file information).
/// </summary>
public sealed class UpdateChecker(HttpClient http)
{
    public const string Repository = "AbdlrhmanDev/OrbitConvert";

    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("OrbitConvert-UpdateCheck");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null; // No releases published yet.
        }
        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken).ConfigureAwait(false);
        if (release?.TagName is null || !Version.TryParse(release.TagName.TrimStart('v', 'V'), out var latest))
        {
            return null;
        }
        var normalizedLatest = Normalize(latest);
        var installerName = $"OrbitConvert-{normalizedLatest}-win-x64.msi";
        var installer = release.Assets.FirstOrDefault(a => string.Equals(a.Name, installerName, StringComparison.OrdinalIgnoreCase));
        var checksums = release.Assets.FirstOrDefault(a => string.Equals(a.Name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
        string? digest = null;
        if (installer?.DownloadUrl is not null && checksums?.DownloadUrl is not null)
        {
            var text = await http.GetStringAsync(checksums.DownloadUrl, cancellationToken).ConfigureAwait(false);
            digest = ParseChecksum(text, installerName);
        }
        return new UpdateInfo(
            Normalize(CurrentVersion),
            normalizedLatest,
            release.HtmlUrl ?? $"https://github.com/{Repository}/releases",
            installer?.DownloadUrl,
            digest);
    }

    internal static string? ParseChecksum(string text, string fileName)
    {
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[0].Length == 64
                && parts[0].All(Uri.IsHexDigit)
                && string.Equals(parts[1].TrimStart('*'), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return parts[0].ToLowerInvariant();
            }
        }
        return null;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? DownloadUrl { get; set; }
    }
}

using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace OrbitConvert.Infrastructure.Updates;

public sealed record UpdateInfo(Version Current, Version Latest, string ReleaseUrl)
{
    public bool IsNewer => Latest > Current;
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
        return new UpdateInfo(Normalize(CurrentVersion), Normalize(latest), release.HtmlUrl ?? $"https://github.com/{Repository}/releases");
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}

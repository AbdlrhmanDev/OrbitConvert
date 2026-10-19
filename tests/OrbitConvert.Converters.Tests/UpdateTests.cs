using System.Net;
using System.Security.Cryptography;
using System.Text;
using OrbitConvert.Infrastructure;
using OrbitConvert.Infrastructure.Updates;

namespace OrbitConvert.Converters.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Checker_finds_the_matching_MSI_and_checksum()
    {
        const string digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/repos/AbdlrhmanDev/OrbitConvert/releases/latest" => Json("""
                {"tag_name":"v99.9.9","html_url":"https://example.test/release","assets":[
                  {"name":"OrbitConvert-99.9.9-win-x64.msi","browser_download_url":"https://example.test/app.msi"},
                  {"name":"SHA256SUMS.txt","browser_download_url":"https://example.test/SHA256SUMS.txt"}]}
                """),
            "/SHA256SUMS.txt" => Text($"{digest}  OrbitConvert-99.9.9-win-x64.msi\n"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var info = await new UpdateChecker(new HttpClient(handler)).CheckAsync(CancellationToken.None);

        Assert.NotNull(info);
        Assert.True(info.IsNewer);
        Assert.True(info.CanInstall);
        Assert.Equal("https://example.test/app.msi", info.InstallerUrl);
        Assert.Equal(digest, info.InstallerSha256);
    }

    [Fact]
    public async Task Installer_download_is_verified_before_it_is_kept()
    {
        var content = Encoding.UTF8.GetBytes("a fake MSI payload for the downloader test");
        var digest = Convert.ToHexStringLower(SHA256.HashData(content));
        var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
        }));
        var installer = new UpdateInstaller(client, new AppPaths(_dir.Path));
        var update = new UpdateInfo(new Version(1, 0, 0), new Version(1, 1, 0), "https://example.test/release",
            "https://example.test/app.msi", digest);

        var path = await installer.DownloadAsync(update, null, CancellationToken.None);

        Assert.Equal(content, File.ReadAllBytes(path));
        Assert.EndsWith("OrbitConvert-1.1.0-win-x64.msi", path);
    }

    [Fact]
    public async Task Installer_deletes_a_download_with_the_wrong_checksum()
    {
        var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3]),
        }));
        var installer = new UpdateInstaller(client, new AppPaths(_dir.Path));
        var update = new UpdateInfo(new Version(1, 0, 0), new Version(1, 1, 0), "https://example.test/release",
            "https://example.test/app.msi", new string('0', 64));

        await Assert.ThrowsAsync<InvalidDataException>(() => installer.DownloadAsync(update, null, CancellationToken.None));

        Assert.Empty(Directory.EnumerateFiles(_dir.Path, "*.download", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(_dir.Path, "*.msi", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("not a checksum", null)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef *app.msi", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void Checksum_parser_accepts_only_a_valid_matching_entry(string line, string? expected)
    {
        Assert.Equal(expected, UpdateChecker.ParseChecksum(line, "app.msi"));
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Text(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "text/plain"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}

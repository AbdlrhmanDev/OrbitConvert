using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Tools;
using OrbitConvert.Infrastructure.Archives;

namespace OrbitConvert.Infrastructure.Tools;

/// <summary>A pinned, checksum-verified download of a third-party tool.</summary>
public sealed record ToolPackage(
    string Name,
    string Version,
    IReadOnlyList<ExternalTool> Provides,
    Uri DownloadUrl,
    string Sha256,
    bool IsArchive,
    string License,
    string Notice);

public static class ToolPackages
{
    // Versions and SHA-256 digests are pinned. A download whose digest differs is rejected and deleted.
    // To update: change the URL and digest together, after verifying the digest from the publisher.
    public static readonly ToolPackage FFmpeg = new(
        "FFmpeg (gyan.dev essentials build)", "9.0.2",
        [ExternalTool.FFmpeg, ExternalTool.FFprobe],
        new Uri("https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.zip"),
        "60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba",
        IsArchive: true,
        "GPL-3.0 (this build includes GPL components such as x264 and x265)",
        "FFmpeg is a trademark of Fabrice Bellard. This build is provided by gyan.dev; source code is available from ffmpeg.org.");

    public static readonly ToolPackage Pandoc = new(
        "Pandoc", "3.12.1",
        [ExternalTool.Pandoc],
        new Uri("https://github.com/jgm/pandoc/releases/download/3.12.1/pandoc-3.12.1-windows-x86_64.zip"),
        "6ef431cb20b2c24a0371fc19200dc49d452be54b1b5c9239f3462841ef53e587",
        IsArchive: true,
        "GPL-2.0-or-later",
        "Pandoc is developed by John MacFarlane and contributors; source code is available at github.com/jgm/pandoc.");

    public static readonly ToolPackage SevenZip = new(
        "7-Zip standalone console (7zr)", "26.04",
        [ExternalTool.SevenZip],
        new Uri("https://github.com/ip7z/7zip/releases/download/26.04/7zr.exe"),
        "256feca8e274e5da655e2a284fabafd9f554365eb164862089dacd4e8276d282",
        IsArchive: false,
        "LGPL-2.1",
        "7-Zip is developed by Igor Pavlov; source code is available at 7-zip.org.");

    public static IReadOnlyList<ToolPackage> All { get; } = [FFmpeg, Pandoc, SevenZip];

    public static ToolPackage? For(ExternalTool tool) => All.FirstOrDefault(p => p.Provides.Contains(tool));
}

public sealed record DownloadProgress(long BytesReceived, long? TotalBytes, string Stage);

/// <summary>
/// Downloads tools only when the user explicitly asks for it. Nothing here runs automatically.
/// </summary>
public sealed class DependencyInstaller(AppPaths paths, IToolLocator locator, HttpClient http, ILogger<DependencyInstaller> logger)
{
    public string InstallDirectory(ToolPackage package) =>
        Path.Combine(paths.ToolsDirectory, package.Provides[0].ToString().ToLowerInvariant());

    public bool IsInstalledByOrbitConvert(ToolPackage package) => Directory.Exists(InstallDirectory(package));

    public async Task InstallAsync(ToolPackage package, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.ToolsDirectory);
        var target = InstallDirectory(package);
        var staging = target + ".partial";
        var download = Path.Combine(paths.ToolsDirectory, $"{Guid.NewGuid():N}.download");

        try
        {
            logger.LogInformation("Downloading {Package} {Version} from {Url}", package.Name, package.Version, package.DownloadUrl);
            await DownloadAsync(package.DownloadUrl, download, progress, cancellationToken).ConfigureAwait(false);

            progress?.Report(new DownloadProgress(0, null, "Verifying checksum"));
            var actual = await ComputeSha256Async(download, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, package.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ConversionException(
                    $"The downloaded {package.Name} failed integrity verification and was deleted. Expected SHA-256 {package.Sha256}, got {actual}.");
            }

            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
            Directory.CreateDirectory(staging);

            if (package.IsArchive)
            {
                progress?.Report(new DownloadProgress(0, null, "Extracting"));
                SafeArchiveExtractor.Extract(download, staging, cancellationToken: cancellationToken);
            }
            else
            {
                File.Move(download, Path.Combine(staging, Path.GetFileName(package.DownloadUrl.LocalPath)));
            }

            File.WriteAllText(Path.Combine(staging, "ORBITCONVERT-SOURCE.txt"),
                $"{package.Name} {package.Version}\r\nDownloaded from: {package.DownloadUrl}\r\nSHA-256: {package.Sha256}\r\nLicense: {package.License}\r\n{package.Notice}\r\n");

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
            Directory.Move(staging, target);
            locator.Refresh();
            logger.LogInformation("Installed {Package} into {Target}", package.Name, target);
        }
        finally
        {
            TryDelete(download);
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, recursive: true); } catch (IOException) { }
            }
        }
    }

    public void Uninstall(ToolPackage package)
    {
        var target = InstallDirectory(package);
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
        locator.Refresh();
    }

    private async Task DownloadAsync(Uri url, string destination, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            received += read;
            progress?.Report(new DownloadProgress(received, total, "Downloading"));
        }
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}

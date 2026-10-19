using System.Diagnostics;
using System.Security.Cryptography;

namespace OrbitConvert.Infrastructure.Updates;

/// <summary>
/// Downloads a release MSI to the local app-data folder and verifies it against the checksum
/// published with the same GitHub release. Installation starts only after an explicit user action.
/// </summary>
public sealed class UpdateInstaller(HttpClient http, AppPaths paths)
{
    public async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!update.CanInstall || update.InstallerUrl is null || update.InstallerSha256 is null)
        {
            throw new InvalidOperationException("This release does not provide a verifiable Windows installer.");
        }
        if (!Uri.TryCreate(update.InstallerUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("The update installer URL is not secure.");
        }

        var directory = Path.Combine(paths.DataRoot, "updates");
        Directory.CreateDirectory(directory);
        var fileName = $"OrbitConvert-{update.Latest}-win-x64.msi";
        var destination = Path.Combine(directory, fileName);
        var temporary = destination + ".download";

        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true))
            {
                var buffer = new byte[1024 * 128];
                long written = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    written += read;
                    if (total is > 0)
                    {
                        progress?.Report(Math.Clamp((double)written / total.Value, 0, 1));
                    }
                }
            }

            string actual;
            await using (var stream = File.OpenRead(temporary))
            {
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            }
            if (!string.Equals(actual, update.InstallerSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The downloaded update failed SHA-256 verification and was deleted.");
            }
            File.Move(temporary, destination, overwrite: true);
            progress?.Report(1);
            return destination;
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    public static void StartInstaller(string msiPath)
    {
        if (!File.Exists(msiPath))
        {
            throw new FileNotFoundException("The update installer was not found.", msiPath);
        }
        var start = new ProcessStartInfo("msiexec.exe")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(msiPath)!,
        };
        start.ArgumentList.Add("/i");
        start.ArgumentList.Add(msiPath);
        start.ArgumentList.Add("/passive");
        start.ArgumentList.Add("/norestart");
        _ = Process.Start(start) ?? throw new InvalidOperationException("Windows Installer could not be started.");
    }
}

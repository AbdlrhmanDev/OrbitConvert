using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using OrbitConvert.Converters.Documents;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Processes;
using OrbitConvert.Core.Tools;
using OrbitConvert.Infrastructure;
using OrbitConvert.Infrastructure.Processes;
using OrbitConvert.Infrastructure.Tools;

namespace OrbitConvert.Converters.Tests;

public class ProcessRunnerTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    public void Dispose() => _dir.Dispose();

    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");
    private static string PowerShell => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    [Fact]
    public async Task Reports_exit_codes_and_output()
    {
        var result = await _runner.RunAsync(new ProcessSpec(Cmd, ["/c", "echo hello & echo oops 1>&2 & exit 3"]), CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.Contains("hello", result.StandardOutputTail);
        Assert.Contains("oops", result.StandardErrorTail);
    }

    [Fact]
    public async Task Missing_executable_is_a_conversion_error()
    {
        var ex = await Assert.ThrowsAsync<ConversionException>(() =>
            _runner.RunAsync(new ProcessSpec(_dir.Combine("does-not-exist.exe"), []), CancellationToken.None));
        Assert.Contains("Could not start", ex.Message);
    }

    [Fact]
    public async Task Arguments_are_passed_verbatim_without_shell_interpretation()
    {
        var script = _dir.Combine("echo-args.ps1");
        File.WriteAllText(script, "[Console]::OutputEncoding = [Text.Encoding]::UTF8; foreach ($a in $args) { [Console]::Out.WriteLine('<' + $a + '>') }",
            new System.Text.UTF8Encoding(true));
        string[] tricky = ["two words", "quote\"inside", "trailing\\", "ünïcødé 名前", "& calc.exe", "%PATH%", "-x"];

        var lines = new List<string>();
        var result = await _runner.RunAsync(new ProcessSpec(PowerShell,
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, .. tricky])
        { OnStandardOutputLine = lines.Add }, CancellationToken.None);

        Assert.True(result.Succeeded, result.StandardErrorTail);
        Assert.Equal(tricky.Select(t => $"<{t}>"), lines);
    }

    [Fact]
    public async Task Cancellation_kills_the_whole_process_tree()
    {
        var marker = $"oc-test-{Guid.NewGuid():N}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        var sw = Stopwatch.StartNew();

        // cmd starts a child ping; both must die.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _runner.RunAsync(new ProcessSpec(Cmd, ["/c", $"title {marker} & ping -n 30 127.0.0.1 > nul"]), cts.Token));

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
        await Task.Delay(500);
        var orphans = Process.GetProcessesByName("PING").Where(p =>
        {
            try { return p.StartTime > DateTime.Now.AddSeconds(-15); } catch { return false; }
        });
        Assert.Empty(orphans);
    }

    [Fact]
    public async Task Timeout_stops_the_process_with_a_message()
    {
        var ex = await Assert.ThrowsAsync<ConversionException>(() =>
            _runner.RunAsync(new ProcessSpec(Cmd, ["/c", "ping -n 30 127.0.0.1 > nul"]) { Timeout = TimeSpan.FromSeconds(1) }, CancellationToken.None));
        Assert.Contains("did not finish", ex.Message);
    }
}

public class ToolLocatorTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string FakeExe(params string[] parts)
    {
        var path = _dir.Combine(parts);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x4D, 0x5A]);
        return path;
    }

    private ToolLocator Locator(Dictionary<ExternalTool, string>? overrides = null) =>
        new(new AppPaths(_dir.Combine("data")), () => overrides ?? [], NullLogger<ToolLocator>.Instance);

    [Fact]
    public void Finds_tools_downloaded_into_the_app_tools_folder()
    {
        var ffmpeg = FakeExe("data", "tools", "ffmpeg", "ffmpeg-9-build", "bin", "ffmpeg.exe");
        var ffprobe = FakeExe("data", "tools", "ffmpeg", "ffmpeg-9-build", "bin", "ffprobe.exe");

        var locator = Locator();

        Assert.Equal(ffmpeg, locator.Find(ExternalTool.FFmpeg));
        Assert.Equal(ffprobe, locator.Find(ExternalTool.FFprobe));
    }

    [Fact]
    public void User_override_wins_and_a_bad_override_is_ignored()
    {
        FakeExe("data", "tools", "pandoc", "pandoc.exe");
        var custom = FakeExe("elsewhere", "my-pandoc.exe");

        Assert.Equal(custom, Locator(new() { [ExternalTool.Pandoc] = custom }).Find(ExternalTool.Pandoc));
        Assert.EndsWith(Path.Combine("tools", "pandoc", "pandoc.exe"),
            Locator(new() { [ExternalTool.Pandoc] = _dir.Combine("missing.exe") }).Find(ExternalTool.Pandoc));
    }

    [Fact]
    public void Refresh_picks_up_newly_installed_tools()
    {
        var locator = Locator();
        Assert.Null(locator.Find(ExternalTool.Ghostscript) is { } gs && gs.StartsWith(_dir.Path) ? gs : null);

        var installed = FakeExe("data", "tools", "ghostscript", "bin", "gswin64c.exe");
        locator.Refresh();

        Assert.Equal(installed, locator.Find(ExternalTool.Ghostscript));
    }
}

public class DependencyInstallerTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Rejects_and_deletes_a_download_whose_checksum_does_not_match()
    {
        var paths = new AppPaths(_dir.Combine("data"));
        var http = new HttpClient(new StubHandler("tampered content"u8.ToArray()));
        var locator = new ToolLocator(paths, () => new Dictionary<ExternalTool, string>(), NullLogger<ToolLocator>.Instance);
        var installer = new DependencyInstaller(paths, locator, http, NullLogger<DependencyInstaller>.Instance);

        var ex = await Assert.ThrowsAsync<ConversionException>(() => installer.InstallAsync(ToolPackages.SevenZip, null, CancellationToken.None));

        Assert.Contains("failed integrity verification", ex.Message);
        Assert.False(installer.IsInstalledByOrbitConvert(ToolPackages.SevenZip));
        Assert.Empty(Directory.EnumerateFiles(paths.ToolsDirectory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Installs_a_download_that_matches_the_pinned_checksum()
    {
        var content = "pretend this is 7zr.exe"u8.ToArray();
        var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content));
        var package = ToolPackages.SevenZip with { Sha256 = digest };
        var paths = new AppPaths(_dir.Combine("data"));
        var locator = new ToolLocator(paths, () => new Dictionary<ExternalTool, string>(), NullLogger<ToolLocator>.Instance);
        var installer = new DependencyInstaller(paths, locator, new HttpClient(new StubHandler(content)), NullLogger<DependencyInstaller>.Instance);

        await installer.InstallAsync(package, null, CancellationToken.None);

        var exe = Path.Combine(installer.InstallDirectory(package), "7zr.exe");
        Assert.Equal(content, File.ReadAllBytes(exe));
        Assert.True(File.Exists(Path.Combine(installer.InstallDirectory(package), "ORBITCONVERT-SOURCE.txt")));
        Assert.Equal(exe, locator.Find(ExternalTool.SevenZip));
    }

    [Fact]
    public void Pinned_packages_have_https_urls_and_sha256_digests()
    {
        Assert.All(ToolPackages.All, p =>
        {
            Assert.Equal("https", p.DownloadUrl.Scheme);
            Assert.Matches("^[0-9a-f]{64}$", p.Sha256);
        });
    }

    private sealed class StubHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }
}

public class DocumentHelperTests
{
    [Fact]
    public void Plain_text_is_escaped_so_markdown_syntax_survives_literally()
    {
        var escaped = PandocConverter.EscapePlainText("# not a heading\n* not a list\n  indented");

        Assert.StartsWith("\\# not a heading\\\n", escaped);
        Assert.Contains("\\* not a list", escaped);
        Assert.Contains("&nbsp;&nbsp;indented", escaped);
    }

    [Fact]
    public void LibreOffice_filters_match_targets()
    {
        Assert.Equal("pdf:writer_web_pdf_Export", LibreOfficeConverter.FilterFor(Core.Formats.KnownFormats.Html, Core.Formats.KnownFormats.Pdf));
        Assert.Equal("docx:MS Word 2007 XML", LibreOfficeConverter.FilterFor(Core.Formats.KnownFormats.Odt, Core.Formats.KnownFormats.Docx));
    }

    [Fact]
    public void Pdf_to_docx_is_marked_experimental_with_limitations()
    {
        var converter = new LibreOfficeConverter(new NoTools(), new NeverRun(), new AppPaths(Path.GetTempPath()));
        var route = converter.Routes.Single(r => r.Inputs.Contains("pdf"));

        Assert.True(route.Experimental);
        Assert.Contains("no OCR", route.Limitations);
        Assert.False(converter.GetAvailability(route).IsAvailable);
    }
}

using System.IO.Compression;
using OrbitConvert.Converters.Archives;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Infrastructure.Archives;

namespace OrbitConvert.Converters.Tests;

public class SafeArchiveExtractorTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string Zip(string name, params (string Entry, byte[] Content)[] entries)
    {
        var path = _dir.Combine(name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            using var s = zip.CreateEntry(entry).Open();
            s.Write(content);
        }
        return path;
    }

    private static byte[] Text(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    [Theory]
    [InlineData("../evil.txt", "traversal")]
    [InlineData("a/../../evil.txt", "traversal")]
    [InlineData("..\\evil.txt", "traversal")]
    [InlineData("/etc/passwd", "absolute")]
    [InlineData("C:/Windows/evil.txt", "absolute")]
    [InlineData("C:evil.txt", "absolute")]
    [InlineData("\\\\server\\share\\evil.txt", "absolute")]
    [InlineData("ok.txt:hidden-stream", "data stream")]
    [InlineData("CON.txt", "reserved")]
    [InlineData("dir/nul", "reserved")]
    [InlineData("trailing.", "dot")]
    public void Rejects_unsafe_entry_names_and_writes_nothing(string entry, string reasonFragment)
    {
        var archive = Zip("bad.zip", ("good.txt", Text("fine")), (entry, Text("pwned")));
        var destination = _dir.Combine("out");

        var ex = Assert.Throws<ConversionException>(() => SafeArchiveExtractor.Extract(archive, destination));

        Assert.Contains("unsafe entry", ex.Message);
        Assert.Contains(reasonFragment, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(_dir.Combine("evil.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "evil.txt")) && File.ReadAllText(Path.Combine(Path.GetTempPath(), "evil.txt")) == "pwned");
    }

    [Fact]
    public void Extracts_nested_folders_and_unicode_names()
    {
        var archive = Zip("ok.zip", ("docs/readme.txt", Text("hello")), ("photos/été/ünï.txt", Text("ü")), ("empty/", []));
        var destination = _dir.Combine("out");

        SafeArchiveExtractor.Extract(archive, destination);

        Assert.Equal("hello", File.ReadAllText(Path.Combine(destination, "docs", "readme.txt")));
        Assert.Equal("ü", File.ReadAllText(Path.Combine(destination, "photos", "été", "ünï.txt")));
        Assert.True(Directory.Exists(Path.Combine(destination, "empty")));
    }

    [Fact]
    public void Skips_symbolic_link_entries()
    {
        var path = _dir.Combine("links.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var link = zip.CreateEntry("link-to-secrets");
            link.ExternalAttributes = unchecked((int)(0xA1FFu << 16)); // S_IFLNK | 0777
            using (var s = link.Open())
            {
                s.Write(Text("/home/user/.ssh/id_rsa"));
            }
            using var f = zip.CreateEntry("real.txt").Open();
            f.Write(Text("data"));
        }
        var destination = _dir.Combine("out");

        SafeArchiveExtractor.Extract(path, destination);

        Assert.False(File.Exists(Path.Combine(destination, "link-to-secrets")));
        Assert.True(File.Exists(Path.Combine(destination, "real.txt")));
    }

    [Fact]
    public void Stops_archives_that_expand_beyond_limits()
    {
        var archive = Zip("bomb.zip", ("zeros.bin", new byte[2_000_000]));
        var limits = new ExtractionLimits { MaxTotalBytes = 1_000_000 };

        var ex = Assert.Throws<ConversionException>(() => SafeArchiveExtractor.Extract(archive, _dir.Combine("out"), limits));
        Assert.Contains("size limit", ex.Message);
    }

    [Fact]
    public void Stops_archives_with_too_many_entries()
    {
        var archive = Zip("many.zip", Enumerable.Range(0, 20).Select(i => ($"f{i}.txt", Text("x"))).ToArray());

        var ex = Assert.Throws<ConversionException>(() =>
            SafeArchiveExtractor.Extract(archive, _dir.Combine("out"), new ExtractionLimits { MaxEntries = 10 }));
        Assert.Contains("more than 10", ex.Message);
    }

    [Fact]
    public void Detects_corrupted_data_through_crc()
    {
        var content = Text(new string('A', 5000) + "UNIQUE-MARKER" + new string('B', 5000));
        var path = _dir.Combine("stored.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var s = zip.CreateEntry("data.txt", CompressionLevel.NoCompression).Open();
            s.Write(content);
        }
        // Flip one byte of the stored (uncompressed) payload.
        var bytes = File.ReadAllBytes(path);
        var marker = System.Text.Encoding.ASCII.GetBytes("UNIQUE-MARKER");
        var at = bytes.AsSpan().IndexOf(marker);
        bytes[at] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        var test = SafeArchiveExtractor.Test(path);
        Assert.False(test.IsValid);
        Assert.Contains(test.Problems, p => p.Contains("CRC") || p.Contains("corrupt", StringComparison.OrdinalIgnoreCase));

        Assert.ThrowsAny<Exception>(() => SafeArchiveExtractor.Extract(path, _dir.Combine("out")));
    }

    [Fact]
    public void Test_reports_valid_archives()
    {
        var archive = Zip("ok.zip", ("a.txt", Text("1")), ("b/c.txt", Text("22")));
        var result = SafeArchiveExtractor.Test(archive);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.UncompressedBytes);
    }

    [Fact]
    public async Task Archive_converter_zips_and_extracts_round_trip_through_the_engine()
    {
        var a = _dir.Combine("one", "same.txt");
        var b = _dir.Combine("two", "same.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(a)!);
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        File.WriteAllText(a, "first");
        File.WriteAllText(b, "second");
        var outDir = _dir.Combine("result");
        var converter = new ArchiveConverter(new NoTools(), new NeverRun());

        var zipped = (await Run.Convert(converter, "zip", [a, b], new ConversionOptions { Archive = { ArchiveName = "bundle" } }, outDir)).AssertSucceeded();
        var zipPath = Assert.Single(zipped.Outputs);
        Assert.Equal(KnownFormats.Zip, Run.Detect(zipPath));
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            Assert.Equal(["same (1).txt", "same.txt"], zip.Entries.Select(e => e.FullName).Order());
        }

        var extracted = (await Run.Convert(converter, ArchiveConverter.ExtractKey, [zipPath])).AssertSucceeded();
        var folder = Assert.Single(extracted.Outputs);
        Assert.Equal("first", File.ReadAllText(Path.Combine(folder, "same.txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(folder, "same (1).txt")));
    }

    [Fact]
    public void Seven_zip_creation_is_unavailable_without_7zip()
    {
        var converter = new ArchiveConverter(new NoTools(), new NeverRun());
        var route = converter.Routes.Single(r => r.TargetKey == "7z");
        var availability = converter.GetAvailability(route);

        Assert.False(availability.IsAvailable);
        Assert.Equal(Core.Tools.ExternalTool.SevenZip, availability.MissingTool);
    }
}

internal sealed class NoTools : Core.Tools.IToolLocator
{
    public string? Find(Core.Tools.ExternalTool tool) => null;
    public void Refresh() { }
}

internal sealed class NeverRun : Core.Processes.IProcessRunner
{
    public Task<Core.Processes.ProcessResult> RunAsync(Core.Processes.ProcessSpec spec, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No process should be started in this test.");
}

using System.IO.Compression;
using System.Text;
using OrbitConvert.Core.Formats;

namespace OrbitConvert.Core.Tests;

public class FileTypeDetectorTests
{
    private readonly FileTypeDetector _detector = new();

    private DetectionResult Detect(byte[] content, string name = "file.bin") =>
        _detector.Detect(new MemoryStream(content), name);

    private static byte[] Bytes(params object[] parts)
    {
        var ms = new MemoryStream();
        foreach (var part in parts)
        {
            switch (part)
            {
                case string s: ms.Write(Encoding.ASCII.GetBytes(s)); break;
                case byte[] b: ms.Write(b); break;
                case int i: ms.WriteByte((byte)i); break;
            }
        }
        ms.Write(new byte[64]); // padding so headers are long enough
        return ms.ToArray();
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("gif")]
    [InlineData("tiff-le")]
    [InlineData("tiff-be")]
    [InlineData("webp")]
    [InlineData("wav")]
    [InlineData("avi")]
    [InlineData("flac")]
    [InlineData("pdf")]
    [InlineData("7z")]
    [InlineData("mp3")]
    [InlineData("aac")]
    [InlineData("rtf")]
    public void Identifies_signatures(string kind)
    {
        var (bytes, expected) = kind switch
        {
            "png" => (Samples.Png, KnownFormats.Png),
            "jpeg" => (Samples.Jpeg, KnownFormats.Jpeg),
            "gif" => (Bytes("GIF89a", 1, 0, 1, 0), KnownFormats.Gif),
            "tiff-le" => (Bytes("II*", 0, 8, 0, 0, 0), KnownFormats.Tiff),
            "tiff-be" => (Bytes("MM", 0, "*", 0, 0, 0, 8), KnownFormats.Tiff),
            "webp" => (Bytes("RIFF", 0, 0, 0, 0, "WEBPVP8 "), KnownFormats.WebP),
            "wav" => (Bytes("RIFF", 0, 0, 0, 0, "WAVEfmt "), KnownFormats.Wav),
            "avi" => (Bytes("RIFF", 0, 0, 0, 0, "AVI LIST"), KnownFormats.Avi),
            "flac" => (Bytes("fLaC", 0, 0, 0, 34), KnownFormats.Flac),
            "pdf" => (Samples.Pdf, KnownFormats.Pdf),
            "7z" => (Bytes(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }), KnownFormats.SevenZip),
            "mp3" => (Bytes("ID3", 4, 0, 0), KnownFormats.Mp3),
            "aac" => (Bytes(0xFF, 0xF1, 0x50, 0x80), KnownFormats.Aac),
            "rtf" => (Bytes("{\\rtf1\\ansi"), KnownFormats.Rtf),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var result = Detect(bytes);

        Assert.Equal(expected, result.Format);
        Assert.Equal(DetectionMethod.Signature, result.Method);
    }

    [Theory]
    [InlineData("isom", "mp4")]
    [InlineData("mp42", "mp4")]
    [InlineData("qt  ", "mov")]
    [InlineData("M4A ", "m4a")]
    [InlineData("heic", "heic")]
    [InlineData("avif", "avif")]
    public void Identifies_iso_media_brands(string brand, string expectedId)
    {
        var bytes = Bytes(0, 0, 0, 24, "ftyp", brand, 0, 0, 0, 0, "isom", "mp41");
        Assert.Equal(expectedId, Detect(bytes).Format?.Id);
    }

    [Fact]
    public void Mif1_with_avif_compatible_brand_is_avif()
    {
        var bytes = Bytes(0, 0, 0, 24, "ftyp", "mif1", 0, 0, 0, 0, "avif", "miaf");
        Assert.Equal(KnownFormats.Avif, Detect(bytes).Format);
    }

    [Fact]
    public void Distinguishes_webm_from_matroska()
    {
        var webm = Bytes(0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x82, 0x84, "webm");
        var mkv = Bytes(0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x82, 0x88, "matroska");
        Assert.Equal(KnownFormats.WebM, Detect(webm).Format);
        Assert.Equal(KnownFormats.Mkv, Detect(mkv).Format);
    }

    [Fact]
    public void Distinguishes_opus_from_vorbis_in_ogg()
    {
        Assert.Equal(KnownFormats.Opus, Detect(Bytes("OggS", 0, 2, new byte[22], "OpusHead")).Format);
        Assert.Equal(KnownFormats.Ogg, Detect(Bytes("OggS", 0, 2, new byte[22], 1, "vorbis")).Format);
    }

    [Fact]
    public void Identifies_office_and_ebook_containers_inside_zip()
    {
        Assert.Equal(KnownFormats.Docx, Detect(Zip(("[Content_Types].xml", "<Types/>"), ("word/document.xml", "<w:document/>")), "x.docx").Format);
        Assert.Equal(KnownFormats.Odt, Detect(Zip(("mimetype", "application/vnd.oasis.opendocument.text"), ("content.xml", "<x/>"))).Format);
        Assert.Equal(KnownFormats.Epub, Detect(Zip(("mimetype", "application/epub+zip"), ("META-INF/container.xml", "<x/>"))).Format);
        Assert.Equal(KnownFormats.Zip, Detect(Zip(("readme.txt", "hello"))).Format);
    }

    [Fact]
    public void Content_wins_over_misleading_extension_and_warns()
    {
        var result = Detect(Samples.Png, "photo.jpg");

        Assert.Equal(KnownFormats.Png, result.Format);
        Assert.NotNull(result.Warning);
        Assert.Contains("JPG", result.Warning);
    }

    [Fact]
    public void Compatible_containers_do_not_warn()
    {
        var m4aAsMp4 = Bytes(0, 0, 0, 20, "ftyp", "M4A ", 0, 0, 0, 0, "isom");
        Assert.Null(Detect(m4aAsMp4, "song.mp4").Warning);
    }

    [Theory]
    [InlineData("notes.md", "md")]
    [InlineData("notes.markdown", "md")]
    [InlineData("readme.txt", "txt")]
    [InlineData("page.html", "html")]
    public void Text_formats_use_extension(string name, string expectedId)
    {
        Assert.Equal(expectedId, Detect(Encoding.UTF8.GetBytes("# Title\n\nSome text é ü\n"), name).Format?.Id);
    }

    [Fact]
    public void Html_is_recognized_without_extension()
    {
        Assert.Equal(KnownFormats.Html, Detect(Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>x</body></html>"), "page").Format);
    }

    [Fact]
    public void Text_starting_with_BM_is_not_a_bitmap()
    {
        Assert.Equal(KnownFormats.Txt, Detect(Encoding.UTF8.GetBytes("BMW owners club newsletter\n"), "note.txt").Format);
    }

    [Fact]
    public void Renamed_binary_is_not_trusted_as_document()
    {
        var garbage = Enumerable.Range(0, 512).Select(i => (byte)(i * 7 % 251)).ToArray();
        garbage[0] = 0x00;
        var result = Detect(garbage, "report.pdf");

        Assert.Null(result.Format);
        Assert.Equal(DetectionMethod.Unknown, result.Method);
    }

    [Fact]
    public void Empty_file_is_unknown()
    {
        Assert.Equal(DetectionMethod.Unknown, Detect([], "empty.png").Method);
    }

    [Fact]
    public void Detects_from_a_real_file_on_disk()
    {
        using var dir = new TempDir();
        var path = dir.File("ünïcødé name.png", Samples.Png);
        Assert.Equal(KnownFormats.Png, _detector.Detect(path).Format);
    }

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var w = new StreamWriter(zip.CreateEntry(name, name == "mimetype" ? CompressionLevel.NoCompression : CompressionLevel.Optimal).Open());
                w.Write(content);
            }
        }
        return ms.ToArray();
    }
}

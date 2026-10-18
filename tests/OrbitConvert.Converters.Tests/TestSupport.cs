using Microsoft.Extensions.Logging.Abstractions;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;

namespace OrbitConvert.Converters.Tests;

public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oc-tests", Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public static class Run
{
    private static readonly IFileTypeDetector Detector = new FileTypeDetector();

    public static ConversionEngine Engine() =>
        new(new TempWorkspaceProvider(), new OutputValidator(Detector), NullLogger<ConversionEngine>.Instance);

    public static InputFile Input(string path) =>
        new(path, Detector.Detect(path).Format, new FileInfo(path).Length);

    /// <summary>Runs a converter route end to end through the real engine (validation and commit included).</summary>
    public static async Task<ConversionResult> Convert(IConverter converter, string targetKey, IReadOnlyList<string> inputs,
        ConversionOptions? options = null, string? outputDir = null, CancellationToken ct = default)
    {
        var files = inputs.Select(Input).ToList();
        var route = converter.Routes.First(r => r.TargetKey == targetKey && files.All(f => r.Accepts(f.Format)));
        var plan = new ConversionPlan(converter, route, files, options ?? new ConversionOptions(), new OutputSettings(outputDir));
        return await Engine().RunAsync(plan, null, ct);
    }

    public static ConversionResult AssertSucceeded(this ConversionResult result)
    {
        Assert.True(result.Succeeded, $"Expected success but got {result.Status}: {result.Error}\n{result.TechnicalDetail}");
        return result;
    }

    public static FileFormat? Detect(string path) => Detector.Detect(path).Format;
}

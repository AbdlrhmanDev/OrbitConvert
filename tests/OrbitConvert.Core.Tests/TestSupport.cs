using Microsoft.Extensions.Logging.Abstractions;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Tools;

namespace OrbitConvert.Core.Tests;

/// <summary>A temporary directory deleted after the test.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oc-tests", Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name, byte[] content)
    {
        var full = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, content);
        return full;
    }

    public string Text(string name, string content) => File(name, System.Text.Encoding.UTF8.GetBytes(content));

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

public static class Samples
{
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R'];
    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0];
    public static readonly byte[] Pdf = "%PDF-1.7\n%âãÏÓ\n1 0 obj\n<<>>\nendobj\n"u8.ToArray();
}

/// <summary>
/// A scriptable converter for engine, registry and queue tests. It writes real files so the engine's
/// validation and commit logic run exactly as in production.
/// </summary>
public sealed class FakeConverter : IConverter
{
    public string Id { get; init; } = "fake";
    public string DisplayName => "Fake";
    public List<ConversionRoute> RouteList { get; } = [];
    public IReadOnlyList<ConversionRoute> Routes => RouteList;
    public Func<ConversionRoute, RouteAvailability> Availability { get; set; } = _ => RouteAvailability.Available;
    public Action<ConversionRoute, IReadOnlyList<InputFile>, ConversionOptions>? OptionsValidation { get; set; }
    public Func<ConversionContext, CancellationToken, Task<IReadOnlyList<string>>>? Behavior { get; set; }

    public RouteAvailability GetAvailability(ConversionRoute route) => Availability(route);

    public void ValidateOptions(ConversionRoute route, IReadOnlyList<InputFile> inputs, ConversionOptions options) =>
        OptionsValidation?.Invoke(route, inputs, options);

    public Task<IReadOnlyList<string>> ExecuteAsync(ConversionContext context, CancellationToken cancellationToken) =>
        Behavior is null ? WritePng(context) : Behavior(context, cancellationToken);

    public static Task<IReadOnlyList<string>> WritePng(ConversionContext context)
    {
        var path = context.DefaultOutputPath();
        File.WriteAllBytes(path, Samples.Png);
        return Task.FromResult<IReadOnlyList<string>>([path]);
    }

    public FakeConverter Route(string key, FileFormat? output, RouteKind kind, params FileFormat[] inputs)
    {
        RouteList.Add(new ConversionRoute
        {
            TargetKey = key,
            Label = key.ToUpperInvariant(),
            Kind = kind,
            Inputs = inputs.Length == 0 ? new HashSet<string> { ConversionRoute.AnyFormat } : ConversionRoute.Formats(inputs),
            Output = output,
            MinInputs = kind == RouteKind.Combine ? 2 : 1,
        });
        return this;
    }

    public static RouteAvailability Missing(ExternalTool tool) => RouteAvailability.Missing(tool);
}

public static class EngineFactory
{
    public static ConversionEngine Create(string? workRoot = null) =>
        new(new TempWorkspaceProvider(workRoot), new OutputValidator(new FileTypeDetector()), NullLogger<ConversionEngine>.Instance);

    public static InputFile Input(string path, FileFormat? format) => new(path, format, new FileInfo(path).Length);
}

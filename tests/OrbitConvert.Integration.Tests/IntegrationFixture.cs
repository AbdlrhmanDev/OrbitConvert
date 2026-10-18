using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrbitConvert.Converters;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Tools;
using OrbitConvert.Infrastructure;

namespace OrbitConvert.Integration.Tests;

/// <summary>
/// The full production service graph (same DI registration as the app and CLI), with an isolated data
/// folder. External tools are discovered the normal way: PATH, standard install locations, or the
/// ORBITCONVERT_TOOLS_DIR environment variable. Tests that need a missing tool are skipped, not failed.
/// </summary>
public sealed class IntegrationFixture : IDisposable
{
    public IntegrationFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "oc-integration", Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(Root);
        Services = new ServiceCollection()
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
            .AddOrbitConvert(new AppPaths(Path.Combine(Root, "data")))
            .BuildServiceProvider();
        Tools = Services.GetRequiredService<IToolLocator>();
        Registry = Services.GetRequiredService<ConversionCapabilityRegistry>();
        Engine = Services.GetRequiredService<ConversionEngine>();
    }

    public string Root { get; }
    public ServiceProvider Services { get; }
    public IToolLocator Tools { get; }
    public ConversionCapabilityRegistry Registry { get; }
    public ConversionEngine Engine { get; }

    public bool Has(ExternalTool tool) => Tools.Find(tool) is not null;

    public string NewDir()
    {
        var dir = Path.Combine(Root, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Plans and runs a conversion exactly like the UI does.</summary>
    public async Task<ConversionResult> ConvertAsync(string input, string target, ConversionOptions? options = null, CancellationToken ct = default)
    {
        var inputs = Registry.Analyze([input]);
        var plan = Assert.Single(Registry.CreatePlans(inputs, target, options ?? new ConversionOptions(), OutputSettings.BesideOriginal, out _));
        return await Engine.RunAsync(plan, null, ct);
    }

    /// <summary>Creates media with FFmpeg's built-in test sources (no binary fixtures in the repo).</summary>
    public string MakeMedia(string dir, string name, params string[] ffmpegArgs)
    {
        var path = Path.Combine(dir, name);
        var psi = new ProcessStartInfo(Tools.Find(ExternalTool.FFmpeg)!) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(ffmpegArgs).Append(path))
        {
            psi.ArgumentList.Add(a);
        }
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, "fixture generation failed: " + err);
        return path;
    }

    /// <summary>Reads stream properties with ffprobe ("width", "height", "codec_name", ...).</summary>
    public string Probe(string file, string stream, string entry)
    {
        var psi = new ProcessStartInfo(Tools.Find(ExternalTool.FFprobe)!) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-v", "error", "-select_streams", stream, "-show_entries", "stream=" + entry, "-of", "default=nw=1:nk=1", file })
        {
            psi.ArgumentList.Add(a);
        }
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        return output;
    }

    public void Dispose()
    {
        Services.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

[CollectionDefinition("integration")]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationFixture>;

public static class ResultExtensions
{
    public static ConversionResult AssertSucceeded(this ConversionResult result)
    {
        Assert.True(result.Succeeded, $"Expected success but got {result.Status}: {result.Error}\n{result.TechnicalDetail}");
        return result;
    }
}

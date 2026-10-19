using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Tools;

namespace OrbitConvert.Core.Tests;

public class ConversionCapabilityRegistryTests
{
    private static InputFile In(FileFormat? format, string name = "file") => new($@"C:\in\{name}{format?.PrimaryExtension}", format, 100);

    private static ConversionCapabilityRegistry Registry(params IConverter[] converters) => new(converters, new FileTypeDetector());

    [Fact]
    public void Offers_only_targets_supported_for_the_input()
    {
        var images = new FakeConverter()
            .Route("png", KnownFormats.Png, RouteKind.Convert, KnownFormats.Jpeg, KnownFormats.WebP)
            .Route("webp", KnownFormats.WebP, RouteKind.Convert, KnownFormats.Jpeg, KnownFormats.Png);
        var audio = new FakeConverter { Id = "audio" }.Route("mp3", KnownFormats.Mp3, RouteKind.Convert, KnownFormats.Wav);

        var targets = Registry(images, audio).GetTargets([In(KnownFormats.Jpeg)]);

        Assert.Equal(["png", "webp"], targets.Select(t => t.Key).Order());
    }

    [Fact]
    public void Target_requires_every_input_to_be_convertible()
    {
        var c = new FakeConverter()
            .Route("png", KnownFormats.Png, RouteKind.Convert, KnownFormats.Jpeg)
            .Route("mp3", KnownFormats.Mp3, RouteKind.Convert, KnownFormats.Wav);

        var targets = Registry(c).GetTargets([In(KnownFormats.Jpeg), In(KnownFormats.Wav)]);

        Assert.Empty(targets);
    }

    [Fact]
    public void Inputs_already_in_the_target_format_do_not_hide_the_target_and_are_skipped()
    {
        var c = new FakeConverter().Route("png", KnownFormats.Png, RouteKind.Convert, KnownFormats.Jpeg);
        var registry = Registry(c);
        IReadOnlyList<InputFile> inputs = [In(KnownFormats.Jpeg, "a"), In(KnownFormats.Png, "b")];

        Assert.Contains(registry.GetTargets(inputs), t => t.Key == "png");

        var plans = registry.CreatePlans(inputs, "png", new ConversionOptions(), OutputSettings.BesideOriginal, out var skipped);
        Assert.Single(plans);
        Assert.Equal("b.png", Assert.Single(skipped).FileName);
    }

    [Fact]
    public void Missing_tool_disables_target_with_explanation()
    {
        var c = new FakeConverter
        {
            Availability = _ => RouteAvailability.Missing(ExternalTool.FFmpeg),
        }.Route("mp3", KnownFormats.Mp3, RouteKind.Convert, KnownFormats.Wav);

        var target = Assert.Single(Registry(c).GetTargets([In(KnownFormats.Wav)]));

        Assert.False(target.IsAvailable);
        Assert.Contains("FFmpeg", target.UnavailableReason);
        Assert.Equal([ExternalTool.FFmpeg], target.MissingTools);
    }

    [Fact]
    public void Available_converter_is_preferred_over_unavailable_one_for_the_same_target()
    {
        var unavailable = new FakeConverter { Id = "a", Availability = _ => RouteAvailability.Unavailable("nope") }
            .Route("pdf", KnownFormats.Pdf, RouteKind.Convert, KnownFormats.Docx);
        var available = new FakeConverter { Id = "b" }.Route("pdf", KnownFormats.Pdf, RouteKind.Convert, KnownFormats.Docx);
        var registry = Registry(unavailable, available);

        Assert.True(Assert.Single(registry.GetTargets([In(KnownFormats.Docx)])).IsAvailable);
        var plan = Assert.Single(registry.CreatePlans([In(KnownFormats.Docx)], "pdf", new(), OutputSettings.BesideOriginal, out _));
        Assert.Same(available, plan.Converter);
    }

    [Fact]
    public void Combine_targets_need_minimum_inputs_and_accepted_formats()
    {
        var c = new FakeConverter().Route("pdf-merge", KnownFormats.Pdf, RouteKind.Combine, KnownFormats.Pdf);
        var registry = Registry(c);

        Assert.Empty(registry.GetTargets([In(KnownFormats.Pdf)]));
        Assert.Single(registry.GetTargets([In(KnownFormats.Pdf, "a"), In(KnownFormats.Pdf, "b")]));
        Assert.Empty(registry.GetTargets([In(KnownFormats.Pdf), In(KnownFormats.Png)]));

        var plans = registry.CreatePlans([In(KnownFormats.Pdf, "a"), In(KnownFormats.Pdf, "b")], "pdf-merge", new(), OutputSettings.BesideOriginal, out _);
        Assert.Equal(2, Assert.Single(plans).Inputs.Count);
    }

    [Fact]
    public void Unknown_files_only_get_wildcard_routes()
    {
        var c = new FakeConverter()
            .Route("png", KnownFormats.Png, RouteKind.Convert, KnownFormats.Jpeg)
            .Route("zip", KnownFormats.Zip, RouteKind.Convert);

        var targets = Registry(c).GetTargets([In(null, "mystery")]);

        Assert.Equal("zip", Assert.Single(targets).Key);
    }

    [Fact]
    public void Unavailable_targets_sort_after_available_ones()
    {
        var c = new FakeConverter
        {
            Availability = r => r.TargetKey == "avif" ? RouteAvailability.Unavailable("no encoder") : RouteAvailability.Available,
        }
            .Route("avif", KnownFormats.Avif, RouteKind.Convert, KnownFormats.Png)
            .Route("jpeg", KnownFormats.Jpeg, RouteKind.Convert, KnownFormats.Png);

        var targets = Registry(c).GetTargets([In(KnownFormats.Png)]);

        Assert.Equal(["jpeg", "avif"], targets.Select(t => t.Key));
    }

    [Fact]
    public void CreatePlans_accepts_extension_aliases()
    {
        var c = new FakeConverter().Route("jpeg", KnownFormats.Jpeg, RouteKind.Convert, KnownFormats.Png);
        var plans = Registry(c).CreatePlans([In(KnownFormats.Png)], "jpg", new(), OutputSettings.BesideOriginal, out _);
        Assert.Equal("jpeg", Assert.Single(plans).Route.TargetKey);
    }

    [Fact]
    public void CreatePlans_rejects_unsupported_and_unavailable_conversions_with_a_message()
    {
        var c = new FakeConverter { Availability = _ => RouteAvailability.Unavailable("Encoder missing.") }
            .Route("mp3", KnownFormats.Mp3, RouteKind.Convert, KnownFormats.Wav);
        var registry = Registry(c);

        var unsupported = Assert.Throws<ConversionException>(() => registry.CreatePlans([In(KnownFormats.Png)], "mp3", new(), OutputSettings.BesideOriginal, out _));
        Assert.Contains("cannot be converted", unsupported.Message);

        var unavailable = Assert.Throws<ConversionException>(() => registry.CreatePlans([In(KnownFormats.Wav)], "mp3", new(), OutputSettings.BesideOriginal, out _));
        Assert.Equal("Encoder missing.", unavailable.Message);

        Assert.Throws<ConversionException>(() => registry.CreatePlans([In(KnownFormats.Wav)], "no-such-target", new(), OutputSettings.BesideOriginal, out _));
    }

    [Fact]
    public void Each_plan_gets_its_own_copy_of_the_options()
    {
        var c = new FakeConverter().Route("png", KnownFormats.Png, RouteKind.Convert, KnownFormats.Jpeg);
        var options = new ConversionOptions();
        var plans = Registry(c).CreatePlans([In(KnownFormats.Jpeg, "a"), In(KnownFormats.Jpeg, "b")], "png", options, OutputSettings.BesideOriginal, out _);

        plans[0].Options.Image.Quality = 10;

        Assert.Equal(90, plans[1].Options.Image.Quality);
        Assert.Equal(90, options.Image.Quality);
    }

    [Fact]
    public void CreatePlans_validates_options_before_returning_a_job()
    {
        var c = new FakeConverter
        {
            OptionsValidation = (_, _, _) => throw new ConversionException("Required option is missing."),
        }.Route("pdf-extract", KnownFormats.Pdf, RouteKind.Transform, KnownFormats.Pdf);

        var ex = Assert.Throws<ConversionException>(() =>
            Registry(c).CreatePlans([In(KnownFormats.Pdf)], "pdf-extract", new(), OutputSettings.BesideOriginal, out _));

        Assert.Equal("Required option is missing.", ex.Message);
    }

    [Fact]
    public void Analyze_identifies_by_content_and_reports_missing_files()
    {
        using var dir = new TempDir();
        var real = dir.File("picture.dat", Samples.Png);
        var registry = Registry();

        Assert.Equal(KnownFormats.Png, registry.Analyze(real).Format);
        var missing = registry.Analyze(Path.Combine(dir.Path, "gone.png"));
        Assert.Null(missing.Format);
        Assert.Contains("does not exist", missing.Detection!.Warning);
    }
}

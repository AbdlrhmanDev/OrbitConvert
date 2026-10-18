using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;

namespace OrbitConvert.Core.Tests;

public class ConversionEngineTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly TempDir _work = new();
    private readonly ConversionEngine _engine;

    public ConversionEngineTests() => _engine = EngineFactory.Create(_work.Path);

    public void Dispose()
    {
        _dir.Dispose();
        _work.Dispose();
    }

    private ConversionPlan Plan(FakeConverter converter, string inputName = "in.jpg", OutputSettings? output = null)
    {
        var input = _dir.File(inputName, Samples.Jpeg);
        return new ConversionPlan(converter, converter.Routes[0], [EngineFactory.Input(input, KnownFormats.Jpeg)], new ConversionOptions(),
            output ?? OutputSettings.BesideOriginal);
    }

    private static FakeConverter PngConverter() => new FakeConverter().Route("png", KnownFormats.Png, RouteKind.Convert, KnownFormats.Jpeg);

    private bool WorkspaceIsEmpty() => !Directory.Exists(_work.Path) || !Directory.EnumerateFileSystemEntries(_work.Path).Any();

    [Fact]
    public async Task Successful_conversion_lands_beside_the_original_and_cleans_up()
    {
        var result = await _engine.RunAsync(Plan(PngConverter()), null, CancellationToken.None);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Equal(Path.Combine(_dir.Path, "in.png"), Assert.Single(result.Outputs));
        Assert.Equal(Samples.Png, File.ReadAllBytes(result.Outputs[0]));
        Assert.True(WorkspaceIsEmpty());
    }

    [Fact]
    public async Task Custom_output_folder_is_created_and_used()
    {
        var target = Path.Combine(_dir.Path, "out", "nested");
        var result = await _engine.RunAsync(Plan(PngConverter(), output: new OutputSettings(target)), null, CancellationToken.None);

        Assert.Equal(Path.Combine(target, "in.png"), Assert.Single(result.Outputs));
    }

    [Fact]
    public async Task Converter_exception_fails_without_leaving_output()
    {
        var converter = PngConverter();
        converter.Behavior = (ctx, _) =>
        {
            File.WriteAllBytes(ctx.DefaultOutputPath(), [1, 2, 3]); // partial output
            throw new ConversionException("Engine crashed.", technicalDetail: "stderr tail");
        };

        var result = await _engine.RunAsync(Plan(converter), null, CancellationToken.None);

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Equal("Engine crashed.", result.Error);
        Assert.Equal("stderr tail", result.TechnicalDetail);
        Assert.False(File.Exists(Path.Combine(_dir.Path, "in.png")));
        Assert.True(WorkspaceIsEmpty());
    }

    [Fact]
    public async Task Cancellation_reports_canceled_and_leaves_nothing_behind()
    {
        var converter = PngConverter();
        var started = new TaskCompletionSource();
        converter.Behavior = async (ctx, ct) =>
        {
            File.WriteAllBytes(ctx.DefaultOutputPath(), [1, 2, 3]);
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        };
        using var cts = new CancellationTokenSource();

        var run = _engine.RunAsync(Plan(converter), null, cts.Token);
        await started.Task;
        cts.Cancel();
        var result = await run;

        Assert.Equal(ConversionStatus.Canceled, result.Status);
        Assert.Empty(Directory.EnumerateFiles(_dir.Path, "*.png"));
        Assert.True(WorkspaceIsEmpty());
    }

    [Fact]
    public async Task Renamed_output_is_caught_by_signature_validation()
    {
        var converter = PngConverter();
        converter.Behavior = (ctx, _) =>
        {
            // A "conversion" that only renames: JPEG bytes in a .png file.
            var path = ctx.DefaultOutputPath();
            File.Copy(ctx.Input.Path, path);
            return Task.FromResult<IReadOnlyList<string>>([path]);
        };

        var result = await _engine.RunAsync(Plan(converter), null, CancellationToken.None);

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Contains("expected PNG", result.Error);
        Assert.False(File.Exists(Path.Combine(_dir.Path, "in.png")));
    }

    [Fact]
    public async Task Empty_or_missing_output_fails()
    {
        var empty = PngConverter();
        empty.Behavior = (ctx, _) =>
        {
            var path = ctx.DefaultOutputPath();
            File.WriteAllBytes(path, []);
            return Task.FromResult<IReadOnlyList<string>>([path]);
        };
        var missing = PngConverter();
        missing.Behavior = (ctx, _) => Task.FromResult<IReadOnlyList<string>>([ctx.DefaultOutputPath()]);
        var nothing = PngConverter();
        nothing.Behavior = (_, _) => Task.FromResult<IReadOnlyList<string>>([]);

        Assert.Contains("empty", (await _engine.RunAsync(Plan(empty, "a.jpg"), null, default)).Error);
        Assert.Contains("did not create", (await _engine.RunAsync(Plan(missing, "b.jpg"), null, default)).Error);
        Assert.Contains("without producing", (await _engine.RunAsync(Plan(nothing, "c.jpg"), null, default)).Error);
    }

    [Fact]
    public async Task Output_outside_the_work_directory_is_rejected()
    {
        var escape = Path.Combine(_dir.Path, "escaped.png");
        var converter = PngConverter();
        converter.Behavior = (_, _) =>
        {
            File.WriteAllBytes(escape, Samples.Png);
            return Task.FromResult<IReadOnlyList<string>>([escape]);
        };

        var result = await _engine.RunAsync(Plan(converter), null, CancellationToken.None);

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Contains("outside its work directory", result.Error);
    }

    [Fact]
    public async Task Existing_output_is_kept_and_new_one_numbered()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "in.png"), "keep me");

        var result = await _engine.RunAsync(Plan(PngConverter()), null, CancellationToken.None);

        Assert.Equal(Path.Combine(_dir.Path, "in (1).png"), Assert.Single(result.Outputs));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(_dir.Path, "in.png")));
    }

    [Fact]
    public async Task Skip_strategy_reports_skipped()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "in.png"), "keep me");

        var result = await _engine.RunAsync(Plan(PngConverter(), output: new OutputSettings(null, CollisionStrategy.Skip)), null, CancellationToken.None);

        Assert.Equal(ConversionStatus.Skipped, result.Status);
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(_dir.Path, "in.png")));
    }

    [Fact]
    public async Task Same_format_transform_never_overwrites_the_input_even_with_overwrite()
    {
        var converter = new FakeConverter().Route("rotate", null, RouteKind.Transform, KnownFormats.Jpeg);
        converter.Behavior = (ctx, _) =>
        {
            var path = Path.Combine(ctx.WorkDirectory, ctx.Input.FileName); // same name as the input
            File.WriteAllBytes(path, Samples.Jpeg.Concat(new byte[] { 1 }).ToArray());
            return Task.FromResult<IReadOnlyList<string>>([path]);
        };
        var plan = Plan(converter, output: new OutputSettings(null, CollisionStrategy.Overwrite));
        var original = File.ReadAllBytes(plan.Inputs[0].Path);

        var result = await _engine.RunAsync(plan, null, CancellationToken.None);

        Assert.Equal(Path.Combine(_dir.Path, "in (1).jpg"), Assert.Single(result.Outputs));
        Assert.Equal(original, File.ReadAllBytes(plan.Inputs[0].Path));
    }

    [Fact]
    public async Task Missing_and_locked_inputs_fail_with_clear_messages()
    {
        var plan = Plan(PngConverter());
        using (new FileStream(plan.Inputs[0].Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var locked = await _engine.RunAsync(plan, null, CancellationToken.None);
            Assert.Contains("in use", locked.Error);
        }

        File.Delete(plan.Inputs[0].Path);
        var missing = await _engine.RunAsync(plan, null, CancellationToken.None);
        Assert.Contains("not found", missing.Error);
    }

    [Fact]
    public async Task Progress_reaches_one_on_success()
    {
        var reports = new List<double>();
        var converter = PngConverter();
        converter.Behavior = (ctx, _) =>
        {
            ctx.ReportProgress(0.5);
            return FakeConverter.WritePng(ctx);
        };

        await _engine.RunAsync(Plan(converter), new InlineProgress(reports.Add), CancellationToken.None);

        Assert.Equal([0.5, 1.0], reports);
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

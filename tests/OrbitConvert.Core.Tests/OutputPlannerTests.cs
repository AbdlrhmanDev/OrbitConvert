using OrbitConvert.Core.Conversion;

namespace OrbitConvert.Core.Tests;

public class OutputPlannerTests
{
    [Fact]
    public void Free_name_is_used_as_is()
    {
        using var dir = new TempDir();
        Assert.Equal(Path.Combine(dir.Path, "photo.png"), OutputPlanner.ChooseDestination(dir.Path, "photo.png", CollisionStrategy.KeepBoth, []));
    }

    [Fact]
    public void KeepBoth_appends_increasing_numbers()
    {
        using var dir = new TempDir();
        dir.Text("photo.png", "x");
        dir.Text("photo (1).png", "x");

        Assert.Equal(Path.Combine(dir.Path, "photo (2).png"), OutputPlanner.ChooseDestination(dir.Path, "photo.png", CollisionStrategy.KeepBoth, []));
    }

    [Fact]
    public void Skip_returns_null_when_the_name_exists()
    {
        using var dir = new TempDir();
        dir.Text("photo.png", "x");
        Assert.Null(OutputPlanner.ChooseDestination(dir.Path, "photo.png", CollisionStrategy.Skip, []));
    }

    [Fact]
    public void Overwrite_reuses_the_name_but_never_an_input_file()
    {
        using var dir = new TempDir();
        var existing = dir.Text("report.pdf", "x");

        Assert.Equal(existing, OutputPlanner.ChooseDestination(dir.Path, "report.pdf", CollisionStrategy.Overwrite, []));
        Assert.Equal(Path.Combine(dir.Path, "report (1).pdf"),
            OutputPlanner.ChooseDestination(dir.Path, "report.pdf", CollisionStrategy.Overwrite, [existing]));
        Assert.Equal(Path.Combine(dir.Path, "report (1).pdf"),
            OutputPlanner.ChooseDestination(dir.Path, "report.pdf", CollisionStrategy.Skip, [existing]));
    }

    [Fact]
    public void Folders_count_as_collisions()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "archive"));
        Assert.Equal(Path.Combine(dir.Path, "archive (1)"), OutputPlanner.ChooseDestination(dir.Path, "archive", CollisionStrategy.Overwrite, []));
    }

    [Fact]
    public void Commit_moves_file_and_retries_with_a_new_name_if_taken_meanwhile()
    {
        using var work = new TempDir();
        using var dest = new TempDir();
        var source = work.Text("out.txt", "new");
        var target = Path.Combine(dest.Path, "out.txt");
        File.WriteAllText(target, "old"); // appeared after the destination was chosen

        var committed = OutputPlanner.Commit(source, target, overwrite: false);

        Assert.Equal(Path.Combine(dest.Path, "out (1).txt"), committed);
        Assert.Equal("old", File.ReadAllText(target));
        Assert.Equal("new", File.ReadAllText(committed));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public void Commit_overwrite_replaces_content()
    {
        using var work = new TempDir();
        using var dest = new TempDir();
        var source = work.Text("out.txt", "new");
        var target = dest.Text("out.txt", "old");

        OutputPlanner.Commit(source, target, overwrite: true);

        Assert.Equal("new", File.ReadAllText(target));
    }

    [Fact]
    public void Commit_moves_folders_with_contents()
    {
        using var work = new TempDir();
        using var dest = new TempDir();
        work.Text(Path.Combine("pages", "a.txt"), "A");
        work.Text(Path.Combine("pages", "sub", "b.txt"), "B");

        var committed = OutputPlanner.Commit(Path.Combine(work.Path, "pages"), Path.Combine(dest.Path, "pages"), overwrite: false);

        Assert.Equal("B", File.ReadAllText(Path.Combine(committed, "sub", "b.txt")));
    }

    [Fact]
    public void Handles_unicode_and_long_paths()
    {
        using var dir = new TempDir();
        var deep = Path.Combine(dir.Path, string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat("nested-folder-with-a-long-name", 8)));
        Directory.CreateDirectory(deep);
        Assert.True(deep.Length > 260);
        File.WriteAllText(Path.Combine(deep, "日本語 файл.txt"), "x");

        var chosen = OutputPlanner.ChooseDestination(deep, "日本語 файл.txt", CollisionStrategy.KeepBoth, []);

        Assert.EndsWith("日本語 файл (1).txt", chosen);
    }
}

public class PageRangeTests
{
    [Theory]
    [InlineData(null, 3, new[] { 0, 1, 2 })]
    [InlineData("", 3, new[] { 0, 1, 2 })]
    [InlineData("2", 3, new[] { 1 })]
    [InlineData("1-2, 5", 5, new[] { 0, 1, 4 })]
    [InlineData("3-", 5, new[] { 2, 3, 4 })]
    [InlineData("-2", 5, new[] { 0, 1 })]
    [InlineData("2,2,1-2", 5, new[] { 1, 0 })]
    public void Parses_ranges(string? range, int count, int[] expected) =>
        Assert.Equal(expected, PageRange.Parse(range, count));

    [Theory]
    [InlineData("0")]
    [InlineData("9")]
    [InlineData("3-1")]
    [InlineData("a")]
    [InlineData(",")]
    public void Rejects_invalid_ranges(string range) =>
        Assert.Throws<ConversionException>(() => PageRange.Parse(range, 5));
}

public class ErrorTranslatorTests
{
    [Fact]
    public void Maps_common_io_failures_to_actionable_messages()
    {
        Assert.Contains("disk space", ErrorTranslator.ToUserMessage(new IOException("full", unchecked((int)0x80070070))));
        Assert.Contains("in use", ErrorTranslator.ToUserMessage(new IOException("locked", unchecked((int)0x80070020))));
        Assert.Contains("Access denied", ErrorTranslator.ToUserMessage(new UnauthorizedAccessException()));
        Assert.Equal("Shown as-is.", ErrorTranslator.ToUserMessage(new ConversionException("Shown as-is.")));
    }

    [Fact]
    public void Recognizes_tool_output()
    {
        Assert.Contains("disk space", ErrorTranslator.FromToolOutput("av_interleaved_write_frame(): No space left on device"));
        Assert.Contains("damaged", ErrorTranslator.FromToolOutput("input.mp4: Invalid data found when processing input"));
        Assert.Null(ErrorTranslator.FromToolOutput("all good"));
    }
}

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using OrbitConvert.Core.Automation;
using OrbitConvert.Infrastructure.Automation;

namespace OrbitConvert.Converters.Tests;

public class FolderAutomationTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private (FolderAutomation Automation, ConcurrentQueue<string> Seen, WatchFolder Watcher) Start(params string[] extensions)
    {
        var watcher = new WatchFolder { Path = _dir.Path, Enabled = true, Extensions = [.. extensions], PresetId = "p" };
        var seen = new ConcurrentQueue<string>();
        var automation = new FolderAutomation(() => [watcher], (_, path) => { seen.Enqueue(Path.GetFileName(path)); return Task.CompletedTask; },
            NullLogger.Instance, TimeSpan.FromMilliseconds(300));
        automation.Reload();
        return (automation, seen, watcher);
    }

    private static async Task WaitFor(Func<bool> condition, int ms = 5000)
    {
        for (var waited = 0; waited < ms && !condition(); waited += 50)
        {
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task Processes_each_matching_new_file_once()
    {
        var (automation, seen, _) = Start(".png");
        using var _ = automation;

        File.WriteAllBytes(Path.Combine(_dir.Path, "a.png"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_dir.Path, "b.txt"), [1]);
        File.WriteAllBytes(Path.Combine(_dir.Path, "c.PNG"), [1]);
        await WaitFor(() => seen.Count >= 2);
        await Task.Delay(800);

        Assert.Equal(["a.png", "c.PNG"], seen.Order());
    }

    [Fact]
    public async Task Ignores_temporary_files_and_its_own_outputs()
    {
        var (automation, seen, _) = Start();
        using var _ = automation;
        var output = Path.Combine(_dir.Path, "photo-small.jpg");
        automation.Ignore(output);

        File.WriteAllBytes(Path.Combine(_dir.Path, "download.crdownload"), [1]);
        File.WriteAllBytes(Path.Combine(_dir.Path, "~$report.docx"), [1]);
        File.WriteAllBytes(Path.Combine(_dir.Path, ".photo.jpg.123.partial"), [1]);
        File.WriteAllBytes(output, [1]);
        File.WriteAllBytes(Path.Combine(_dir.Path, "real.jpg"), [1]);
        await WaitFor(() => seen.Count >= 1);
        await Task.Delay(800);

        Assert.Equal(["real.jpg"], seen);
    }

    [Fact]
    public async Task Waits_until_a_file_is_no_longer_being_written()
    {
        var (automation, seen, _) = Start(".mov");
        using var _ = automation;
        var path = Path.Combine(_dir.Path, "clip.mov");

        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            for (var i = 0; i < 6; i++)
            {
                stream.Write(new byte[1024]);
                stream.Flush();
                await Task.Delay(150);
                Assert.Empty(seen); // still growing / still open
            }
        }
        await WaitFor(() => !seen.IsEmpty);

        Assert.Equal(["clip.mov"], seen);
    }

    [Fact]
    public async Task Disabled_watchers_and_missing_folders_do_nothing()
    {
        var (automation, seen, watcher) = Start();
        using var _ = automation;
        watcher.Enabled = false;
        automation.Reload();
        Assert.Equal(0, automation.ActiveCount);

        File.WriteAllBytes(Path.Combine(_dir.Path, "x.jpg"), [1]);
        await Task.Delay(800);
        Assert.Empty(seen);

        watcher.Enabled = true;
        watcher.Path = Path.Combine(_dir.Path, "gone");
        automation.Reload();
        Assert.Equal(0, automation.ActiveCount);
        Assert.Contains("does not exist", watcher.LastError);
    }

    [Theory]
    [InlineData(".heic, png; *.JPG", new[] { ".heic", ".png", ".jpg" })]
    [InlineData("", new string[0])]
    public void Parses_extension_lists(string text, string[] expected) =>
        Assert.Equal(expected, WatchFolder.ParseExtensions(text));
}

using System.Text.RegularExpressions;

namespace OrbitConvert.Core.Tests;

public sealed partial class LocalizationXamlTests
{
    [Fact]
    public void Enum_window_properties_do_not_use_localized_string_resources()
    {
        var root = FindRepositoryRoot();
        var views = Path.Combine(root, "src", "OrbitConvert.App", "Views");
        var offenders = Directory.EnumerateFiles(views, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Path: path, Line: index + 1, Text: line)))
            .Where(item => LocalizedSizeToContent().IsMatch(item.Text))
            .Select(item => $"{Path.GetRelativePath(root, item.Path)}:{item.Line}")
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OrbitConvert.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the OrbitConvert repository root.");
    }

    [GeneratedRegex("SizeToContent\\s*=\\s*\"\\{DynamicResource\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex LocalizedSizeToContent();
}

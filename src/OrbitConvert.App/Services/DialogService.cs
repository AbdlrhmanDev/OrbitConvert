using System.Windows;
using Microsoft.Win32;
using OrbitConvert.Core.Formats;

namespace OrbitConvert.App.Services;

/// <summary>Native file pickers and message boxes, kept out of view models for testability.</summary>
public sealed class DialogService
{
    public IReadOnlyList<string> PickFiles(string title, FormatCategory? category = null, bool multiple = true)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Multiselect = multiple,
            Filter = BuildFilter(category),
            CheckFileExists = true,
        };
        return dialog.ShowDialog(Owner()) == true ? dialog.FileNames : [];
    }

    public IReadOnlyList<string> PickFiles(string title, IReadOnlyList<FileFormat> formats, bool multiple = true)
    {
        var patterns = string.Join(";", formats.SelectMany(f => f.Extensions).Select(e => "*" + e));
        var dialog = new OpenFileDialog
        {
            Title = title,
            Multiselect = multiple,
            Filter = $"Supported files|{patterns}|All files|*.*",
            CheckFileExists = true,
        };
        return dialog.ShowDialog(Owner()) == true ? dialog.FileNames : [];
    }

    public string? PickFolder(string title, string? initial = null)
    {
        var dialog = new OpenFolderDialog { Title = title, InitialDirectory = initial ?? "" };
        return dialog.ShowDialog(Owner()) == true ? dialog.FolderName : null;
    }

    public string? PickExecutable(string title, string fileName)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = $"{fileName}|{fileName}|Programs|*.exe", CheckFileExists = true };
        return dialog.ShowDialog(Owner()) == true ? dialog.FileName : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(Owner()!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    public void Info(string title, string message) =>
        MessageBox.Show(Owner()!, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Error(string title, string message) =>
        MessageBox.Show(Owner()!, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    private static Window? Owner() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
        ?? Application.Current?.MainWindow;

    private static string BuildFilter(FormatCategory? category)
    {
        var formats = category is null ? KnownFormats.All : KnownFormats.All.Where(f => f.Category == category).ToList();
        var patterns = string.Join(";", formats.SelectMany(f => f.Extensions).Select(e => "*" + e));
        var name = category is null ? "Supported files" : $"{category} files";
        return $"{name}|{patterns}|All files|*.*";
    }
}

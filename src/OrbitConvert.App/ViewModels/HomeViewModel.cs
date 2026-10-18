using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrbitConvert.App.Services;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.History;
using OrbitConvert.Shell;

namespace OrbitConvert.App.ViewModels;

public sealed record QuickCategory(string Title, string Description, string Glyph, string Category, FormatCategory? Filter);

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly DialogService _dialogs;
    private readonly IHistoryStore _history;
    private readonly Lazy<IMainWindowHost> _host;

    public HomeViewModel(DialogService dialogs, IHistoryStore history, Lazy<IMainWindowHost> host)
    {
        _dialogs = dialogs;
        _history = history;
        _host = host;
        _history.Changed += (_, _) => Ui.Post(RefreshRecent);
        RefreshRecent();
    }

    public Action<Page>? Navigate { get; set; }

    public IReadOnlyList<QuickCategory> Categories { get; } =
    [
        new("Images", "JPG, PNG, WebP, HEIC, AVIF…", "", "Image", FormatCategory.Image),
        new("Video", "MP4, MKV, MOV, WebM, GIF", "", "Video", FormatCategory.Video),
        new("Audio", "MP3, WAV, FLAC, AAC, Opus…", "", "Audio", FormatCategory.Audio),
        new("Documents", "DOCX, ODT, Markdown, HTML…", "", "Document", FormatCategory.Document),
        new("PDF", "Merge, split, rotate, images", "", "Pdf", FormatCategory.Pdf),
        new("Archives", "ZIP and 7z", "", "Archive", FormatCategory.Archive),
    ];

    public ObservableCollection<HistoryEntry> Recent { get; } = [];

    [ObservableProperty]
    private bool _hasRecent;

    public void AcceptDrop(IReadOnlyList<string> files) => _host.Value.ShowConvert(files);

    [RelayCommand]
    private void SelectFiles()
    {
        var files = _dialogs.PickFiles("Choose files to convert");
        if (files.Count > 0)
        {
            _host.Value.ShowConvert(files);
        }
    }

    [RelayCommand]
    private void OpenCategory(QuickCategory category)
    {
        var files = _dialogs.PickFiles($"Choose {category.Title.ToLowerInvariant()} to convert", category.Filter);
        if (files.Count > 0)
        {
            _host.Value.ShowConvert(files);
        }
    }

    [RelayCommand]
    private void Reveal(HistoryEntry entry)
    {
        var target = entry.Outputs.FirstOrDefault(p => File.Exists(p) || Directory.Exists(p)) ?? entry.Inputs.FirstOrDefault();
        if (target is not null)
        {
            ShellLauncher.RevealInExplorer(target);
        }
    }

    [RelayCommand]
    private void ShowHistory() => Navigate?.Invoke(Page.History);

    [RelayCommand]
    private void ShowQueue() => Navigate?.Invoke(Page.Convert);

    private void RefreshRecent()
    {
        Recent.Clear();
        foreach (var entry in _history.Entries.Take(6))
        {
            Recent.Add(entry);
        }
        HasRecent = Recent.Count > 0;
    }
}

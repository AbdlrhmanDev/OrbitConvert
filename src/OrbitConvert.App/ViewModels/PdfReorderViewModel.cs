using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Pdf;

namespace OrbitConvert.App.ViewModels;

public sealed partial class PdfPageViewModel(int sourceIndex) : ObservableObject
{
    public int SourceIndex { get; } = sourceIndex;
    public string Label => $"Page {SourceIndex + 1}";
    [ObservableProperty] private int _rotation;
    [ObservableProperty] private BitmapImage? _thumbnail;
}

public sealed partial class PdfReorderViewModel : ObservableObject
{
    private readonly IPdfEditor _editor;
    private readonly CancellationTokenSource _cancellation = new();

    public PdfReorderViewModel(string inputPath, IPdfEditor editor)
    {
        InputPath = Path.GetFullPath(inputPath);
        _editor = editor;
        var count = editor.ReadPageCount(InputPath);
        for (var i = 0; i < count; i++) Pages.Add(new PdfPageViewModel(i));
        _ = LoadThumbnailsAsync();
    }

    public string InputPath { get; }
    public string FileName => Path.GetFileName(InputPath);
    public ObservableCollection<PdfPageViewModel> Pages { get; } = [];
    [ObservableProperty] private PdfPageViewModel? _selectedPage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Drag with Move up/down, rotate or remove pages, then save a copy.";

    public event EventHandler? CloseRequested;
    public event EventHandler<ImageEditSavedEventArgs>? Saved;

    private async Task LoadThumbnailsAsync()
    {
        try
        {
            foreach (var page in Pages)
            {
                var bytes = await _editor.RenderThumbnailAsync(InputPath, page.SourceIndex, _cancellation.Token);
                using var stream = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                page.Thumbnail = bitmap;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = ErrorTranslator.ToUserMessage(ex); }
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedPage is null) return;
        var index = Pages.IndexOf(SelectedPage);
        if (index > 0) Pages.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedPage is null) return;
        var index = Pages.IndexOf(SelectedPage);
        if (index >= 0 && index < Pages.Count - 1) Pages.Move(index, index + 1);
    }

    [RelayCommand]
    private void Rotate()
    {
        if (SelectedPage is not null) SelectedPage.Rotation = (SelectedPage.Rotation + 90) % 360;
    }

    [RelayCommand]
    private void Remove()
    {
        if (SelectedPage is null || Pages.Count <= 1) return;
        var index = Pages.IndexOf(SelectedPage);
        Pages.Remove(SelectedPage);
        SelectedPage = Pages[Math.Min(index, Pages.Count - 1)];
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var request = new PdfEditRequest(Pages.Select(p => new PdfPageEdit(p.SourceIndex, p.Rotation)).ToArray());
            var output = await _editor.SaveAsync(InputPath, request, _cancellation.Token);
            Saved?.Invoke(this, new ImageEditSavedEventArgs(output, false));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = ErrorTranslator.ToUserMessage(ex); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellation.Cancel();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

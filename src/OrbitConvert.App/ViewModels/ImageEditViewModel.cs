using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Images;

namespace OrbitConvert.App.ViewModels;

public sealed partial class MetadataEntryViewModel(ImageMetadataEntry entry) : ObservableObject
{
    public string Key { get; } = entry.Key;
    public string Value { get; } = entry.Value;
    public bool IsGps { get; } = entry.IsGps;
    public string Label => IsGps ? $"📍 {Key}" : Key;
    [ObservableProperty] private bool _isSelected;
}

public sealed partial class ImageEditViewModel : ObservableObject
{
    private readonly IImageEditor _editor;
    private readonly CancellationTokenSource _cancellation = new();

    public ImageEditViewModel(string inputPath, IImageEditor editor, string initialMode)
    {
        InputPath = Path.GetFullPath(inputPath);
        _editor = editor;
        var size = editor.ReadDimensions(InputPath);
        Width = size.Width;
        Height = size.Height;
        InitialMode = initialMode;
        foreach (var entry in editor.ReadMetadata(InputPath)) Metadata.Add(new MetadataEntryViewModel(entry));
        _ = RefreshPreviewAsync();
    }

    public string InputPath { get; }
    public string FileName => Path.GetFileName(InputPath);
    public string InitialMode { get; }
    public IReadOnlyList<int> Rotations { get; } = [0, 90, 180, 270];
    public IReadOnlyList<ImageFlip> Flips { get; } = Enum.GetValues<ImageFlip>();
    public IReadOnlyList<ImageAnnotationKind> AnnotationKinds { get; } = Enum.GetValues<ImageAnnotationKind>();
    public ObservableCollection<ImageAnnotation> Annotations { get; } = [];
    public ObservableCollection<ImageRedaction> Redactions { get; } = [];
    public ObservableCollection<MetadataEntryViewModel> Metadata { get; } = [];

    [ObservableProperty] private int _width;
    [ObservableProperty] private int _height;
    [ObservableProperty] private int _rotationDegrees;
    [ObservableProperty] private ImageFlip _flip;
    [ObservableProperty] private int _brightness;
    [ObservableProperty] private int _contrast;
    [ObservableProperty] private string _annotationText = "Note";
    [ObservableProperty] private ImageAnnotationKind _annotationKind = ImageAnnotationKind.Text;
    [ObservableProperty] private string _annotationColor = "#FF3B81";
    [ObservableProperty] private double _redactX = 25;
    [ObservableProperty] private double _redactY = 25;
    [ObservableProperty] private double _redactWidth = 30;
    [ObservableProperty] private double _redactHeight = 20;
    [ObservableProperty] private BitmapImage? _preview;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorText;

    public event EventHandler? CloseRequested;
    public event EventHandler<ImageEditSavedEventArgs>? Saved;

    private ImageEditRequest Request() => new(
        Width > 0 ? Width : null,
        Height > 0 ? Height : null,
        RotationDegrees,
        Flip,
        Brightness,
        Contrast,
        Annotations.ToArray(),
        Redactions.ToArray());

    [RelayCommand]
    private async Task RefreshPreviewAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorText = null;
        try
        {
            var bytes = await _editor.PreviewAsync(InputPath, Request(), _cancellation.Token);
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            Preview = bitmap;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorText = ErrorTranslator.ToUserMessage(ex); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task AddTextAsync()
    {
        if (!string.IsNullOrWhiteSpace(AnnotationText))
        {
            Annotations.Add(new ImageAnnotation(ImageAnnotationKind.Text, .08, .15, .92, .28, AnnotationColor, 3, AnnotationText, 38));
            await RefreshPreviewAsync();
        }
    }

    [RelayCommand]
    private async Task AddShapeAsync()
    {
        var kind = AnnotationKind == ImageAnnotationKind.Text ? ImageAnnotationKind.Rectangle : AnnotationKind;
        Annotations.Add(new ImageAnnotation(kind, .12, .15, .88, .70, AnnotationColor, 5,
            kind == ImageAnnotationKind.Text ? AnnotationText : null, 38));
        await RefreshPreviewAsync();
    }

    [RelayCommand]
    private async Task AddRedactionAsync()
    {
        Redactions.Add(new ImageRedaction(RedactX / 100, RedactY / 100, RedactWidth / 100, RedactHeight / 100));
        await RefreshPreviewAsync();
    }

    [RelayCommand]
    private async Task ClearOverlaysAsync()
    {
        Annotations.Clear();
        Redactions.Clear();
        await RefreshPreviewAsync();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorText = null;
        try
        {
            var output = await _editor.EditAsync(InputPath, Request(), _cancellation.Token);
            Saved?.Invoke(this, new ImageEditSavedEventArgs(output, false));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorText = ErrorTranslator.ToUserMessage(ex); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RemoveSelectedMetadataAsync()
    {
        var keys = Metadata.Where(x => x.IsSelected).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        if (keys.Count == 0) { ErrorText = "Select at least one metadata field."; return; }
        await SaveMetadataAsync(keys, false);
    }

    [RelayCommand]
    private Task RemoveAllMetadataAsync() => SaveMetadataAsync(new HashSet<string>(), true);

    private async Task SaveMetadataAsync(IReadOnlySet<string> keys, bool all)
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorText = null;
        try
        {
            var output = await _editor.RemoveMetadataAsync(InputPath, keys, all, _cancellation.Token);
            Saved?.Invoke(this, new ImageEditSavedEventArgs(output, false));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorText = ErrorTranslator.ToUserMessage(ex); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellation.Cancel();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

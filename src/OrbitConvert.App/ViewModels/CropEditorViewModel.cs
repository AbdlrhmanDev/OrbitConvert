using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Images;

namespace OrbitConvert.App.ViewModels;

public sealed record ImageEditSavedEventArgs(string Path, bool ContinueWithBackground);

public sealed partial class AspectChoice(string label, double ratio) : ObservableObject
{
    public string Label { get; } = label;

    /// <summary>Width ÷ height; 0 means free.</summary>
    public double Ratio { get; } = ratio;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Floating crop editor: drag the frame, resize from the corners, pick a ratio, apply.</summary>
public sealed partial class CropEditorViewModel : ObservableObject
{
    private readonly IImageEditor _editor;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Stack<(int X, int Y, int W, int H)> _undo = new();

    public CropEditorViewModel(string inputPath, IImageEditor editor)
    {
        InputPath = Path.GetFullPath(inputPath);
        _editor = editor;
        var dimensions = editor.ReadDimensions(InputPath);
        ImageWidth = dimensions.Width;
        ImageHeight = dimensions.Height;
        Ratios = [new("Free", 0), new("1:1", 1), new("16:9", 16.0 / 9), new("4:3", 4.0 / 3), new("3:4", 3.0 / 4)];
        Ratios[0].IsSelected = true;
        SetCrop(0, 0, ImageWidth, ImageHeight);
    }

    public string InputPath { get; }
    public string FileName => Path.GetFileName(InputPath);
    public int ImageWidth { get; }
    public int ImageHeight { get; }
    public ObservableCollection<AspectChoice> Ratios { get; }

    /// <summary>The non-destructive output name, shown before saving (numbered if it already exists).</summary>
    public string OutputName => $"{Path.GetFileNameWithoutExtension(InputPath)}-crop{Path.GetExtension(InputPath)}";

    [ObservableProperty] private int _cropX;
    [ObservableProperty] private int _cropY;
    [ObservableProperty] private int _cropWidth;
    [ObservableProperty] private int _cropHeight;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool _canUndo;

    public double SelectedRatio => Ratios.FirstOrDefault(r => r.IsSelected)?.Ratio ?? 0;

    public event EventHandler? CloseRequested;
    public event EventHandler<ImageEditSavedEventArgs>? Saved;

    public void SetCrop(int x, int y, int width, int height)
    {
        CropWidth = Math.Clamp(width, 1, ImageWidth);
        CropHeight = Math.Clamp(height, 1, ImageHeight);
        CropX = Math.Clamp(x, 0, ImageWidth - CropWidth);
        CropY = Math.Clamp(y, 0, ImageHeight - CropHeight);
    }

    /// <summary>Records the current frame so the next change can be undone (call when a drag starts).</summary>
    public void Checkpoint()
    {
        _undo.Push((CropX, CropY, CropWidth, CropHeight));
        CanUndo = true;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undo.TryPop(out var previous))
        {
            SetCrop(previous.X, previous.Y, previous.W, previous.H);
        }
        CanUndo = _undo.Count > 0;
    }

    [RelayCommand]
    private void Reset()
    {
        Checkpoint();
        SelectRatio(Ratios[0]);
        SetCrop(0, 0, ImageWidth, ImageHeight);
    }

    [RelayCommand]
    private void PickRatio(AspectChoice choice)
    {
        SelectRatio(choice);
        if (choice.Ratio <= 0)
        {
            return;
        }
        Checkpoint();
        // The largest centered frame with this ratio, at 80 % of the image like the design.
        var width = ImageWidth * 0.8;
        var height = width / choice.Ratio;
        if (height > ImageHeight * 0.8)
        {
            height = ImageHeight * 0.8;
            width = height * choice.Ratio;
        }
        SetCrop((int)Math.Round((ImageWidth - width) / 2), (int)Math.Round((ImageHeight - height) / 2),
            (int)Math.Round(width), (int)Math.Round(height));
    }

    private void SelectRatio(AspectChoice choice)
    {
        foreach (var r in Ratios)
        {
            r.IsSelected = ReferenceEquals(r, choice);
        }
        OnPropertyChanged(nameof(SelectedRatio));
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        ErrorText = null;
        try
        {
            var output = await _editor.CropAsync(InputPath, new CropEditRequest(CropX, CropY, CropWidth, CropHeight), _cancellation.Token);
            Saved?.Invoke(this, new ImageEditSavedEventArgs(output, false));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorText = ErrorTranslator.ToUserMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellation.Cancel();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

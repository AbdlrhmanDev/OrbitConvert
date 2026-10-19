using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Images;

namespace OrbitConvert.App.ViewModels;

public sealed partial class BackgroundSwatch(string name, string color1, string color2) : ObservableObject
{
    public string Name { get; } = name;
    public string Color1 { get; } = color1;
    public string Color2 { get; } = color2;
    public bool IsGradient => !string.Equals(Color1, Color2, StringComparison.OrdinalIgnoreCase);

    public Brush Preview => IsGradient
        ? new LinearGradientBrush((Color)ColorConverter.ConvertFromString(Color1), (Color)ColorConverter.ConvertFromString(Color2), new Point(0, 0), new Point(1, 1))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString(Color1));

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Floating "Add background" editor: gradient or solid canvas, padding, corner radius and shadow,
/// with a live preview. The saved PNG keeps the image's native resolution (it is never upscaled).
/// </summary>
public sealed partial class BackgroundEditorViewModel : ObservableObject
{
    private const double PreviewMaxWidth = 412, PreviewMaxHeight = 472;
    private readonly IImageEditor _editor;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ImageDimensions _source;

    public BackgroundEditorViewModel(string inputPath, IImageEditor editor)
    {
        InputPath = Path.GetFullPath(inputPath);
        _editor = editor;
        _source = editor.ReadDimensions(InputPath);
        Swatches =
        [
            new("Lime", "#C6F432", "#4FD1E8"), new("Violet", "#B68CFF", "#FF7AC6"), new("Ocean", "#6EA8FF", "#B68CFF"),
            new("Ember", "#FF6B5E", "#E8D36A"), new("Ink", "#0B0C10", "#0B0C10"), new("Paper", "#F4F4EF", "#F4F4EF"),
            new("Graphite", "#2C303B", "#2C303B"),
        ];
        Aspects = [new("Auto", 0), new("1:1", 1), new("16:9", 16.0 / 9), new("4:5", 4.0 / 5)];
        Reset();
    }

    public string InputPath { get; }
    public string FileName => Path.GetFileName(InputPath);
    public ObservableCollection<BackgroundSwatch> Swatches { get; }
    public ObservableCollection<AspectChoice> Aspects { get; }
    public string OutputName => $"{Path.GetFileNameWithoutExtension(InputPath)}-bg.png";

    [ObservableProperty] private string _color1 = "#C6F432";
    [ObservableProperty] private string _color2 = "#4FD1E8";
    [ObservableProperty] private string? _customColor;

    /// <summary>Padding as a percentage of the image's shorter side.</summary>
    [ObservableProperty] private int _paddingPercent;
    [ObservableProperty] private int _cornerRadius;
    [ObservableProperty] private int _shadowBlur;
    [ObservableProperty] private int _shadowOpacity;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorText;

    private double SelectedAspect => Aspects.FirstOrDefault(a => a.IsSelected)?.Ratio ?? 0;
    private int PaddingPixels => (int)Math.Round(Math.Min(_source.Width, _source.Height) * PaddingPercent / 100.0);

    /// <summary>Export size: image plus padding, widened or heightened to the chosen canvas ratio.</summary>
    public (int Width, int Height) ExportSize
    {
        get
        {
            var w = _source.Width + 2 * PaddingPixels;
            var h = _source.Height + 2 * PaddingPixels;
            var ratio = SelectedAspect;
            if (ratio > 0)
            {
                if ((double)w / h > ratio) h = (int)Math.Ceiling(w / ratio);
                else w = (int)Math.Ceiling(h * ratio);
            }
            return (w, h);
        }
    }

    public string ExportText => $"{ExportSize.Width} × {ExportSize.Height} px";

    // Live preview geometry, in DIPs.
    private double PreviewScale => Math.Min(PreviewMaxWidth / ExportSize.Width, PreviewMaxHeight / ExportSize.Height);
    public double PreviewCanvasWidth => ExportSize.Width * PreviewScale;
    public double PreviewCanvasHeight => ExportSize.Height * PreviewScale;
    public double PreviewImageWidth => _source.Width * PreviewScale;
    public double PreviewImageHeight => _source.Height * PreviewScale;
    public CornerRadius PreviewCorner => new(Math.Min(CornerRadius, Math.Min(_source.Width, _source.Height) / 2.0) * PreviewScale);
    public double PreviewShadowBlur => ShadowBlur * PreviewScale * 2;
    public double PreviewShadowDepth => ShadowBlur / 3.0 * PreviewScale;
    public double PreviewShadowOpacity => ShadowOpacity / 100.0;

    /// <summary>A true 135° gradient across the preview canvas, matching what ImageMagick renders.</summary>
    public Brush PreviewBrush
    {
        get
        {
            try
            {
                var first = (Color)ColorConverter.ConvertFromString(Color1);
                var second = (Color)ColorConverter.ConvertFromString(Color2);
                if (first == second)
                {
                    return new SolidColorBrush(first);
                }
                var w = PreviewCanvasWidth;
                var h = PreviewCanvasHeight;
                var half = (w + h) / 4;
                return new LinearGradientBrush(first, second, new Point(w / 2 - half, h / 2 - half), new Point(w / 2 + half, h / 2 + half))
                {
                    MappingMode = BrushMappingMode.Absolute,
                };
            }
            catch (FormatException)
            {
                return Brushes.Transparent;
            }
        }
    }

    public event EventHandler? CloseRequested;
    public event EventHandler<ImageEditSavedEventArgs>? Saved;

    partial void OnColor1Changed(string value) => Refresh();
    partial void OnColor2Changed(string value) => Refresh();
    partial void OnPaddingPercentChanged(int value) => Refresh();
    partial void OnCornerRadiusChanged(int value) => Refresh();
    partial void OnShadowBlurChanged(int value) => Refresh();
    partial void OnShadowOpacityChanged(int value) => Refresh();

    private void Refresh()
    {
        foreach (var name in new[]
                 {
                     nameof(PreviewBrush), nameof(ExportText), nameof(PreviewCanvasWidth), nameof(PreviewCanvasHeight),
                     nameof(PreviewImageWidth), nameof(PreviewImageHeight), nameof(PreviewCorner), nameof(PreviewShadowBlur),
                     nameof(PreviewShadowDepth), nameof(PreviewShadowOpacity),
                 })
        {
            OnPropertyChanged(name);
        }
    }

    [RelayCommand]
    private void PickSwatch(BackgroundSwatch swatch)
    {
        foreach (var s in Swatches)
        {
            s.IsSelected = ReferenceEquals(s, swatch);
        }
        CustomColor = null;
        (Color1, Color2) = (swatch.Color1, swatch.Color2);
    }

    /// <summary>Uses the standard Windows color dialog for a custom solid background.</summary>
    [RelayCommand]
    private void PickCustomColor()
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }
        var hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        foreach (var s in Swatches)
        {
            s.IsSelected = false;
        }
        CustomColor = hex;
        (Color1, Color2) = (hex, hex);
    }

    [RelayCommand]
    private void PickAspect(AspectChoice choice)
    {
        foreach (var a in Aspects)
        {
            a.IsSelected = ReferenceEquals(a, choice);
        }
        Refresh();
    }

    [RelayCommand]
    private void Reset()
    {
        PickSwatch(Swatches[0]);
        PickAspect(Aspects[0]);
        PaddingPercent = 12;
        CornerRadius = 14;
        ShadowBlur = 40;
        ShadowOpacity = 45;
        ErrorText = null;
        Refresh();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        ErrorText = null;
        try
        {
            var (width, height) = ExportSize;
            var request = new BackgroundEditRequest(Color1, Color2, !string.Equals(Color1, Color2, StringComparison.OrdinalIgnoreCase),
                PaddingPixels, CornerRadius, ShadowOpacity > 0 && ShadowBlur > 0, ShadowBlur, ShadowOpacity, width, height);
            var output = await _editor.AddBackgroundAsync(InputPath, request, _cancellation.Token);
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

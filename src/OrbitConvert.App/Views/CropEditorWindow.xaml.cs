using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using OrbitConvert.App.ViewModels;

namespace OrbitConvert.App.Views;

public partial class CropEditorWindow : Window
{
    private double _scale = 1;
    private Rect _image;
    private Point? _moveStart;
    private (int X, int Y)? _cropStart;

    public CropEditorWindow(CropEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelChanged;
        Loaded += (_, _) => UpdateOverlay();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                viewModel.CancelCommand.Execute(null);
                e.Handled = true;
            }
        };
        Closed += (_, _) => viewModel.PropertyChanged -= OnViewModelChanged;
    }

    private CropEditorViewModel ViewModel => (CropEditorViewModel)DataContext;

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement { TemplatedParent: Button } && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CropEditorViewModel.CropX) or nameof(CropEditorViewModel.CropY)
            or nameof(CropEditorViewModel.CropWidth) or nameof(CropEditorViewModel.CropHeight))
        {
            Dispatcher.BeginInvoke(UpdateOverlay);
        }
    }

    /// <summary>Positions the frame over the uniformly scaled preview and dims everything outside it.</summary>
    private void UpdateOverlay()
    {
        var hostW = PreviewHost.ActualWidth;
        var hostH = PreviewHost.ActualHeight;
        if (hostW <= 0 || hostH <= 0)
        {
            return;
        }
        _scale = Math.Min(hostW / ViewModel.ImageWidth, hostH / ViewModel.ImageHeight);
        var w = ViewModel.ImageWidth * _scale;
        var h = ViewModel.ImageHeight * _scale;
        _image = new Rect((hostW - w) / 2, (hostH - h) / 2, w, h);

        var frame = new Rect(_image.X + ViewModel.CropX * _scale, _image.Y + ViewModel.CropY * _scale,
            Math.Max(1, ViewModel.CropWidth * _scale), Math.Max(1, ViewModel.CropHeight * _scale));
        Canvas.SetLeft(CropBox, frame.X);
        Canvas.SetTop(CropBox, frame.Y);
        CropBox.Width = frame.Width;
        CropBox.Height = frame.Height;
        Dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(_image), new RectangleGeometry(frame));
    }

    private void OnCropMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Thumb or FrameworkElement { TemplatedParent: Thumb })
        {
            return;
        }
        ViewModel.Checkpoint();
        _moveStart = e.GetPosition(CropCanvas);
        _cropStart = (ViewModel.CropX, ViewModel.CropY);
        CropBox.CaptureMouse();
        e.Handled = true;
    }

    private void OnCropMouseMove(object sender, MouseEventArgs e)
    {
        if (_moveStart is not { } start || _cropStart is not { } crop || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        var current = e.GetPosition(CropCanvas);
        var dx = (int)Math.Round((current.X - start.X) / _scale);
        var dy = (int)Math.Round((current.Y - start.Y) / _scale);
        ViewModel.SetCrop(crop.X + dx, crop.Y + dy, ViewModel.CropWidth, ViewModel.CropHeight);
    }

    private void OnCropMouseUp(object sender, MouseButtonEventArgs e)
    {
        _moveStart = null;
        _cropStart = null;
        CropBox.ReleaseMouseCapture();
    }

    private void OnHandleStart(object sender, DragStartedEventArgs e) => ViewModel.Checkpoint();

    /// <summary>Resizes from a corner; the opposite corner stays fixed and a chosen ratio is kept.</summary>
    private void OnHandleDrag(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { Tag: string handle })
        {
            return;
        }
        var dx = e.HorizontalChange / _scale;
        var dy = e.VerticalChange / _scale;
        double left = ViewModel.CropX, top = ViewModel.CropY;
        double right = left + ViewModel.CropWidth, bottom = top + ViewModel.CropHeight;
        if (handle.Contains('L')) left = Math.Clamp(left + dx, 0, right - 8);
        if (handle.Contains('R')) right = Math.Clamp(right + dx, left + 8, ViewModel.ImageWidth);
        if (handle.Contains('T')) top = Math.Clamp(top + dy, 0, bottom - 8);
        if (handle.Contains('B')) bottom = Math.Clamp(bottom + dy, top + 8, ViewModel.ImageHeight);

        var ratio = ViewModel.SelectedRatio;
        if (ratio > 0)
        {
            var width = right - left;
            var height = width / ratio;
            // Fit inside the image when the ratio-derived height would overflow.
            var maxHeight = handle.Contains('T') ? bottom : ViewModel.ImageHeight - top;
            if (height > maxHeight)
            {
                height = maxHeight;
                width = height * ratio;
                if (handle.Contains('L')) left = right - width; else right = left + width;
            }
            if (handle.Contains('T')) top = bottom - height; else bottom = top + height;
        }

        ViewModel.SetCrop((int)Math.Round(left), (int)Math.Round(top), (int)Math.Round(right - left), (int)Math.Round(bottom - top));
        e.Handled = true;
    }
}

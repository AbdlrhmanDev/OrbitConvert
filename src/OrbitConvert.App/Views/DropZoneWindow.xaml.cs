using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace OrbitConvert.App.Views;

/// <summary>
/// Activation mode A: a small always-on-top circle that accepts files dragged from File Explorer
/// through standard OLE drag-and-drop, then opens the radial menu at the drop point.
/// </summary>
public partial class DropZoneWindow : Window
{
    public DropZoneWindow()
    {
        InitializeComponent();
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += (_, _) => Highlight(false);
        Drop += OnDrop;
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
            DragMove();
            PositionChanged?.Invoke(this, new Point(Left, Top));
        };
    }

    public event EventHandler<IReadOnlyList<string>>? FilesDropped;
    public event EventHandler? OpenRequested;
    public event EventHandler? HideRequested;
    public event EventHandler<Point>? PositionChanged;

    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
        Highlight(HasFiles(e));
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        // Copy effect: OrbitConvert reads the files; it never moves or deletes the originals.
        e.Effects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        Highlight(false);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            FilesDropped?.Invoke(this, files);
        }
        e.Handled = true;
    }

    private void Highlight(bool on)
    {
        var target = on ? 1.18 : 1.0;
        var duration = TimeSpan.FromMilliseconds(120);
        RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(target, duration));
        RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(target, duration));
        Ring.Opacity = on ? 1 : 0.55;
    }

    private void OnOpenClick(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);

    private void OnHideClick(object sender, RoutedEventArgs e) => HideRequested?.Invoke(this, EventArgs.Empty);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter)
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}

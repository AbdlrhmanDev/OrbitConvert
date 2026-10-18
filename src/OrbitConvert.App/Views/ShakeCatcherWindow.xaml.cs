using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OrbitConvert.Shell;

namespace OrbitConvert.App.Views;

/// <summary>
/// Activation mode D (experimental): a temporary drop target that appears under the cursor when
/// the mouse is shaken during a drag. It disappears when the drag ends without a drop on it.
/// </summary>
public partial class ShakeCatcherWindow : Window
{
    private readonly PixelPoint _anchor;
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private int _releasedTicks;

    public ShakeCatcherWindow(PixelPoint anchor)
    {
        InitializeComponent();
        _anchor = anchor;
        SourceInitialized += (_, _) => Place();
        DpiChanged += (_, _) => Place();
        Loaded += (_, _) =>
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
            _watch.Start();
        };
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            _watch.Stop();
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                FilesDropped?.Invoke(this, files);
            }
            Close();
        };
        // The drag ended elsewhere: the left button is up for a few consecutive checks.
        _watch.Tick += (_, _) =>
        {
            _releasedTicks = ScreenGeometry.IsLeftButtonDown() ? 0 : _releasedTicks + 1;
            if (_releasedTicks >= 3)
            {
                _watch.Stop();
                Close();
            }
        };
        Closed += (_, _) => _watch.Stop();
    }

    public event EventHandler<IReadOnlyList<string>>? FilesDropped;

    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var monitor = ScreenGeometry.MonitorAt(_anchor);
        var size = (int)Math.Ceiling(Width * monitor.DpiScale);
        ScreenGeometry.MoveWindow(hwnd, ScreenGeometry.CenterClamped(_anchor, size, size, monitor.WorkArea));
    }
}

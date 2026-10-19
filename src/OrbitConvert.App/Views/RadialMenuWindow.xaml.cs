using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using OrbitConvert.App.ViewModels;
using OrbitConvert.Shell;

namespace OrbitConvert.App.Views;

public partial class RadialMenuWindow : Window
{
    private readonly PixelPoint _anchor;
    private readonly System.Windows.Threading.DispatcherTimer _outsideClickWatch = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private bool _closing;
    private bool _buttonWasDown = true;

    private readonly bool _reduceMotion;

    public RadialMenuWindow(RadialMenuViewModel viewModel, PixelPoint anchor, bool reduceMotion = false)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        _anchor = anchor;
        _reduceMotion = reduceMotion;

        viewModel.CloseRequested += (_, _) => FadeOutAndClose();

        SourceInitialized += (_, _) => PlaceAtAnchor();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PlaceAtAnchor);
        SizeChanged += (_, _) => PlaceAtAnchor();
        Loaded += (_, _) => AnimateIn();
        Deactivated += (_, _) => OnDeactivated();
        PreviewKeyDown += OnPreviewKeyDown;
        DragEnter += OnFileDrag;
        DragOver += OnFileDrag;
        Drop += OnFileDrop;
        _outsideClickWatch.Tick += (_, _) => WatchOutsideClicks();
        Closed += (_, _) => _outsideClickWatch.Stop();
    }

    public RadialMenuViewModel ViewModel { get; }

    /// <summary>Centers the window on the anchor in physical pixels, clamped to that monitor's work area.</summary>
    private void PlaceAtAnchor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
        {
            return;
        }
        var monitor = ScreenGeometry.MonitorAt(_anchor);
        var size = (int)Math.Ceiling(ViewModel.Diameter * monitor.DpiScale);
        var topLeft = ScreenGeometry.CenterClamped(_anchor, size, size, monitor.WorkArea);
        ScreenGeometry.MoveWindow(hwnd, topLeft);
    }

    /// <summary>Identity motion: open 140 ms scaling 0.92 → 1 with a fast-out curve; reduced motion fades only.</summary>
    private void AnimateIn()
    {
        var ease = new BezierEase(0.2, 0.9, 0.1, 1);
        var duration = TimeSpan.FromMilliseconds(140);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        if (_reduceMotion)
        {
            RootScale.ScaleX = RootScale.ScaleY = 1;
        }
        else
        {
            RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, duration) { EasingFunction = ease });
            RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, duration) { EasingFunction = ease });
        }
        Activate();
        Focus();
        _outsideClickWatch.Start();
    }

    /// <summary>
    /// Windows may refuse to activate the menu (foreground lock, e.g. after a drop from Explorer),
    /// in which case no Deactivated event will arrive. Watching the global mouse state keeps
    /// "click outside to dismiss" working either way.
    /// </summary>
    private void WatchOutsideClicks()
    {
        var down = ScreenGeometry.IsLeftButtonDown();
        if (down && !_buttonWasDown && !IsCursorOverWindow())
        {
            FadeOutAndClose();
        }
        _buttonWasDown = down;
    }

    private bool IsCursorOverWindow()
    {
        var cursor = ScreenGeometry.CursorPosition();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
        {
            return false;
        }
        var monitor = ScreenGeometry.MonitorAt(cursor);
        var topLeft = PointToScreen(new Point(0, 0));
        var size = ViewModel.Diameter * monitor.DpiScale;
        return cursor.X >= topLeft.X && cursor.X <= topLeft.X + size && cursor.Y >= topLeft.Y && cursor.Y <= topLeft.Y + size;
    }

    public void FadeOutAndClose()
    {
        if (_closing)
        {
            return;
        }
        _closing = true;
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(100));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    private void OnDeactivated()
    {
        // Clicking elsewhere dismisses the menu. A running conversion continues in the queue and
        // finishes with a notification.
        if (!_closing && IsLoaded && !ScreenGeometry.IsLeftButtonDown())
        {
            FadeOutAndClose();
        }
    }

    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private void OnFileDrag(object sender, DragEventArgs e)
    {
        if (!ViewModel.IsChoosing || !HasFiles(e))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        var item = Wheel.SelectAt(e.GetPosition(Wheel));
        e.Effects = item is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (!ViewModel.IsChoosing || !HasFiles(e)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] dropped
            || !SameFiles(dropped, ViewModel.Paths))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var item = Wheel.SelectAt(e.GetPosition(Wheel));
        // The radial menu was created for this selection by a supported activation path. The OLE
        // payload is used only to confirm this is a real file drop; conversion always uses the
        // already signature-validated ViewModel inputs.
        e.Effects = item is not null && Wheel.ActivateSelected() ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static bool SameFiles(IEnumerable<string> dropped, IReadOnlyList<string> expected)
    {
        try
        {
            var actual = dropped.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var planned = expected.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return actual.SetEquals(planned);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                FadeOutAndClose();
                break;
            case Key.Left or Key.Up:
                ViewModel.MoveSelection(-1);
                break;
            case Key.Right or Key.Down:
                ViewModel.MoveSelection(+1);
                break;
            case Key.Tab:
                ViewModel.MoveSelection(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                break;
            case Key.Enter or Key.Space:
                if (ViewModel.IsChoosing && ViewModel.Selected is not null)
                {
                    ViewModel.Activate(null);
                }
                else if (ViewModel.IsSucceeded)
                {
                    ViewModel.RevealOutputCommand.Execute(null);
                }
                break;
            case >= Key.D1 and <= Key.D9:
                var index = e.Key - Key.D1;
                if (index < ViewModel.Items.Count)
                {
                    ViewModel.Activate(ViewModel.Items[index]);
                }
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}

/// <summary>A cubic-bezier easing curve, so motion can match the identity's CSS timing functions.</summary>
internal sealed class BezierEase : EasingFunctionBase
{
    private readonly double x1, y1, x2, y2;

    public BezierEase(double x1, double y1, double x2, double y2)
    {
        (this.x1, this.y1, this.x2, this.y2) = (x1, y1, x2, y2);
        // EaseIn makes WPF use EaseInCore directly; the curve itself encodes the ease-out shape.
        EasingMode = EasingMode.EaseIn;
    }

    protected override double EaseInCore(double t)
    {
        // Solve x(u) = t for the curve parameter u by bisection, then return y(u).
        double lo = 0, hi = 1, u = t;
        for (var i = 0; i < 24; i++)
        {
            u = (lo + hi) / 2;
            if (Bezier(u, x1, x2) < t) lo = u; else hi = u;
        }
        return Bezier(u, y1, y2);
    }

    private static double Bezier(double u, double p1, double p2) =>
        3 * (1 - u) * (1 - u) * u * p1 + 3 * (1 - u) * u * u * p2 + u * u * u;

    protected override Freezable CreateInstanceCore() => new BezierEase(x1, y1, x2, y2);
}

using System.ComponentModel;
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

    public RadialMenuWindow(RadialMenuViewModel viewModel, PixelPoint anchor)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        _anchor = anchor;

        viewModel.CloseRequested += (_, _) => FadeOutAndClose();
        viewModel.PropertyChanged += OnViewModelChanged;

        SourceInitialized += (_, _) => PlaceAtAnchor();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PlaceAtAnchor);
        SizeChanged += (_, _) => PlaceAtAnchor();
        Loaded += (_, _) => AnimateIn();
        Deactivated += (_, _) => OnDeactivated();
        PreviewKeyDown += OnPreviewKeyDown;
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

    private void AnimateIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(170);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(0.86, 1, duration) { EasingFunction = ease });
        RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(0.86, 1, duration) { EasingFunction = ease });
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
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(120));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
        RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.92, TimeSpan.FromMilliseconds(120)));
        RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.92, TimeSpan.FromMilliseconds(120)));
    }

    private void OnDeactivated()
    {
        // Clicking elsewhere dismisses the menu. A running conversion continues in the queue and
        // finishes with a notification.
        if (!_closing && IsLoaded)
        {
            FadeOutAndClose();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RadialMenuViewModel.Progress))
        {
            var p = ViewModel.Progress;
            PercentText.Text = p < 0 ? "…" : $"{p * 100:0}%";
        }
    }

    private void OnItemMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RadialItemViewModel item })
        {
            ViewModel.SelectItem(item);
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
                if (ViewModel.IsChoosing)
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

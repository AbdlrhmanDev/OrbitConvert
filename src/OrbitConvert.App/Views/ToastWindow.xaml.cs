using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OrbitConvert.App.Views;

/// <summary>
/// OrbitConvert's own notification: a small top-most card in the bottom-right corner of the work
/// area. Unlike tray balloon tips, Windows never moves it silently into the notification center.
/// It never takes focus and closes by itself after a few seconds (not while the pointer is over it).
/// </summary>
public partial class ToastWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private static readonly Brush ErrorBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5E)));

    private readonly DispatcherTimer _timer;
    private readonly bool _reduceMotion;
    private bool _closing;

    public ToastWindow(string title, string message, bool canOpenFolder, bool isError, bool reduceMotion, TimeSpan duration)
    {
        InitializeComponent();
        _reduceMotion = reduceMotion;
        TitleText.Text = title;
        MessageText.Text = message;
        MessageText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        OpenFolderButton.Visibility = canOpenFolder ? Visibility.Visible : Visibility.Collapsed;
        if (isError)
        {
            MarkArc.Stroke = ErrorBrush;
            MarkDot.Fill = ErrorBrush;
        }

        _timer = new DispatcherTimer { Interval = duration };
        _timer.Tick += (_, _) => FadeOutAndClose();
        Card.MouseEnter += (_, _) => _timer.Stop();
        Card.MouseLeave += (_, _) => _timer.Start();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExNoActivate | WsExToolWindow);
        };
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth;
            Top = area.Bottom - ActualHeight;
            AnimateIn();
            _timer.Start();
        };
    }

    public event EventHandler? OpenFolderRequested;
    public event EventHandler? Clicked;

    private void AnimateIn()
    {
        Opacity = 0;
        var duration = TimeSpan.FromMilliseconds(_reduceMotion ? 120 : 220);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration));
        if (!_reduceMotion)
        {
            Slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(16, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    public void FadeOutAndClose()
    {
        if (_closing)
        {
            return;
        }
        _closing = true;
        _timer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        OpenFolderRequested?.Invoke(this, EventArgs.Empty);
        FadeOutAndClose();
    }

    private void OnDismiss(object sender, RoutedEventArgs e) => FadeOutAndClose();

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindButton(source))
        {
            return; // the buttons handle their own clicks
        }
        Clicked?.Invoke(this, EventArgs.Empty);
        FadeOutAndClose();
    }

    private static bool FindButton(DependencyObject node)
    {
        for (var n = node; n is not null; n = n is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(n) : LogicalTreeHelper.GetParent(n))
        {
            if (n is System.Windows.Controls.Button)
            {
                return true;
            }
        }
        return false;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLong(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLong(nint hwnd, int index, nint value);
}

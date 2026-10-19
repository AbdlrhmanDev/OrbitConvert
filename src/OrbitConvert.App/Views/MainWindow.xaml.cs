using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using OrbitConvert.App.Services;
using OrbitConvert.App.ViewModels;

namespace OrbitConvert.App.Views;

public partial class MainWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    private readonly ThemeManager _themeManager;
    private readonly LanguageManager _languageManager;

    public MainWindow(MainViewModel viewModel, ThemeManager themeManager, LanguageManager languageManager)
    {
        InitializeComponent();
        _themeManager = themeManager;
        _languageManager = languageManager;
        ViewModel = viewModel;
        DataContext = viewModel;
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += (_, _) => DropOverlay.Visibility = Visibility.Collapsed;
        Drop += OnDrop;
        SourceInitialized += (_, _) => ApplyTitleBarColors();
        StateChanged += (_, _) => UpdateMaximizedState();
        _themeManager.Changed += OnThemeChanged;
        _languageManager.Enable(this);
        Closed += (_, _) => _themeManager.Changed -= OnThemeChanged;
    }

    public MainViewModel ViewModel { get; }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTitleBarColors();

    /// <summary>
    /// The title bar is drawn by OrbitConvert; DWM still draws the window border and, for a moment
    /// during resize, the caption. Keep both on the identity palette instead of the Windows accent.
    /// Unsupported DWM attributes are ignored on older Windows builds. COLORREF is 0x00BBGGRR.
    /// </summary>
    private void ApplyTitleBarColors()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
        {
            return;
        }
        var darkMode = _themeManager.IsDark ? 1 : 0;
        var captionColor = _themeManager.IsDark ? 0x00181311 : 0x00E5ECEC; // #111318 / #ECECE5
        var textColor = _themeManager.IsDark ? 0x00EEF3F2 : 0x00100C0B;    // #F2F3EE / #0B0C10
        var borderColor = _themeManager.IsDark ? 0x00332A26 : 0x00D6DEDE;  // #262A33 / #DEDED6
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionColor, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref textColor, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    /// <summary>A maximized WindowChrome window extends past the screen by the resize border; inset the content.</summary>
    private void UpdateMaximizedState()
    {
        var maximized = WindowState == WindowState.Maximized;
        var frame = SystemParameters.WindowResizeBorderThickness;
        Root.Margin = maximized ? new Thickness(frame.Left + 1, frame.Top + 1, frame.Right + 1, frame.Bottom + 1) : new Thickness(0);
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
        MaximizeGlyph.Data = Geometry.Parse(maximized ? "M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z" : "M0.5,0.5 H9.5 V9.5 H0.5 Z");
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    // Command palette
    private void OnSearchOpened(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        }, System.Windows.Threading.DispatcherPriority.Input);

    private void OnSearchClosed(object? sender, EventArgs e) => ViewModel.CloseSearchCommand.Execute(null);

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                ViewModel.MoveSearchSelection(1);
                SearchList.ScrollIntoView(ViewModel.SelectedResult);
                e.Handled = true;
                break;
            case Key.Up:
                ViewModel.MoveSearchSelection(-1);
                SearchList.ScrollIntoView(ViewModel.SelectedResult);
                e.Handled = true;
                break;
            case Key.Enter:
                ViewModel.RunSearchResultCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                ViewModel.CloseSearchCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnSearchResultClick(object sender, MouseButtonEventArgs e)
    {
        if (SearchList.SelectedItem is SearchCommand command)
        {
            ViewModel.RunSearchResultCommand.Execute(command);
        }
    }

    // Drag and drop
    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (HasFiles(e))
        {
            DropOverlay.Visibility = Visibility.Visible;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            ViewModel.ShowConvert(files, null);
        }
    }
}

using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrbitConvert.App.Services;
using OrbitConvert.App.ViewModels;
using OrbitConvert.App.Views;
using OrbitConvert.Converters;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Queue;
using OrbitConvert.Core.Settings;
using OrbitConvert.Infrastructure;
using OrbitConvert.Infrastructure.Logging;
using OrbitConvert.Shell;

namespace OrbitConvert.App;

public partial class App : Application, IMainWindowHost
{
    private ServiceProvider? _services;
    private SingleInstance? _instance;
    private ILogger<App>? _logger;
    private MainWindow? _mainWindow;
    private bool _exiting;
    private bool _trayHintShown;

    // Explorer starts one process per selected file for a context-menu verb. The running instance
    // collects requests that arrive close together and shows a single radial menu for all of them.
    private readonly List<string> _pendingRadialFiles = [];
    private DispatcherTimer? _radialDebounce;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = StartupArguments.Parse(e.Args);
        var paths = AppPaths.CreateDefault();
        paths.EnsureCreated();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables("ORBITCONVERT_")
            .Build();
        var minimumLevel = configuration.GetValue("Logging:MinimumLevel", LogLevel.Information);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => b.SetMinimumLevel(minimumLevel).AddDebug().AddProvider(new FileLoggerProvider(paths.LogsDirectory, minimumLevel)));
        services.AddOrbitConvert(paths);
        RegisterAppServices(services);
        _services = services.BuildServiceProvider();
        _logger = _services.GetRequiredService<ILogger<App>>();

        if (args.Command is StartupArguments.RegisterShell or StartupArguments.UnregisterShell)
        {
            RunShellRegistration(args.Command);
            Shutdown();
            return;
        }

        _instance = new SingleInstance(_logger);
        if (!_instance.IsPrimary)
        {
            if (_instance.TrySend(new InstanceMessage(args.Command, args.Files.Select(Path.GetFullPath).ToList()), TimeSpan.FromSeconds(3)))
            {
                Shutdown();
                return;
            }
            MessageBox.Show("OrbitConvert is already running but is not responding.", "OrbitConvert", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        _logger.LogInformation("OrbitConvert {Version} starting ({Command})", typeof(App).Assembly.GetName().Version, args.Command);

        var settings = _services.GetRequiredService<ISettingsStore>();
        _services.GetRequiredService<ThemeManager>().Apply(settings.Current.Theme);
        // Stale-workspace cleanup can touch many files after a crash. Keep it off the UI thread so
        // the main window and Explorer context-menu activation are never delayed by disk cleanup.
        _ = Task.Run(() => _services.GetRequiredService<TempWorkspaceProvider>().CleanupStale(TimeSpan.FromDays(1)));

        var coordinator = _services.GetRequiredService<ConversionCoordinator>();
        var radial = _services.GetRequiredService<RadialMenuService>();
        coordinator.IsResultVisible = () => radial.IsShowingResult || _mainWindow is { IsActive: true };

        var notifications = _services.GetRequiredService<NotificationService>();
        var activation = _services.GetRequiredService<ActivationService>();
        notifications.OpenRequested += (_, _) => ShowMain();
        notifications.ExitRequested += (_, _) => ExitApplication();
        notifications.ToggleDropZoneRequested += (_, _) =>
        {
            var s = settings.Current;
            s.DropZoneEnabled = !activation.IsDropZoneVisible;
            settings.Save(s);
        };
        notifications.ShowTrayIcon(() => activation.IsDropZoneVisible);
        activation.Start();
        // Folder automation: only watchers the user turned on are active.
        _services.GetRequiredService<AutomationService>().Start();

        _instance.MessageReceived += (_, message) => Dispatcher.BeginInvoke(() => Handle(message.Command, message.Files));
        _instance.StartListening();

        // Warm up the FFmpeg encoder probe so the first radial menu opens instantly.
        _ = Task.Run(() => _services.GetRequiredService<OrbitConvert.Converters.Media.FfmpegCapabilities>().Encoders);

        var command = args.Command == StartupArguments.Show && settings.Current.StartMinimizedToTray ? StartupArguments.Background : args.Command;
        Handle(command, args.Files);
        _ = CheckForUpdatesIfEnabledAsync();
    }

    /// <summary>Opt-in only: at most once a day, ask GitHub for the latest release number.</summary>
    private async Task CheckForUpdatesIfEnabledAsync()
    {
        var services = _services!;
        var store = services.GetRequiredService<ISettingsStore>();
        var settings = store.Current;
        if (!settings.CheckForUpdates || settings.LastUpdateCheck is { } last && DateTimeOffset.Now - last < TimeSpan.FromDays(1))
        {
            return;
        }
        try
        {
            var info = await services.GetRequiredService<Infrastructure.Updates.UpdateChecker>().CheckAsync(CancellationToken.None);
            settings.LastUpdateCheck = DateTimeOffset.Now;
            store.Save(settings);
            if (info is { IsNewer: true })
            {
                services.GetRequiredService<NotificationService>().Show("OrbitConvert update available",
                    $"Version {info.Latest} is available. See Settings → Updates.", null);
            }
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException)
        {
            _logger?.LogInformation(ex, "Update check failed");
        }
    }

    private void RegisterAppServices(IServiceCollection services)
    {
        services.AddSingleton<IMainWindowHost>(this);
        services.AddSingleton(sp => new Lazy<IMainWindowHost>(() => sp.GetRequiredService<IMainWindowHost>()));
        services.AddSingleton<ThemeManager>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<ConversionCoordinator>();
        services.AddSingleton<ImageEditorService>();
        services.AddSingleton<RadialMenuService>();
        services.AddSingleton(sp => new Lazy<RadialMenuService>(() => sp.GetRequiredService<RadialMenuService>()));
        services.AddSingleton<ExplorerSelection>();
        services.AddSingleton<ActivationService>();
        services.AddSingleton<PresetRunner>();
        services.AddSingleton<AutomationService>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<ConvertViewModel>();
        services.AddSingleton<BatchViewModel>();
        services.AddSingleton<ToolsViewModel>();
        services.AddSingleton<AutomationViewModel>();
        services.AddSingleton<PresetsViewModel>();
        services.AddSingleton(sp => new Lazy<PresetsViewModel>(() => sp.GetRequiredService<PresetsViewModel>()));
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
    }

    private void Handle(string command, IReadOnlyList<string> files)
    {
        switch (command)
        {
            case StartupArguments.Radial when files.Count > 0:
                QueueRadial(files);
                break;
            case StartupArguments.Open when files.Count > 0:
                ShowConvert(files);
                break;
            case StartupArguments.Background:
                break;
            default:
                ShowMain();
                break;
        }
    }

    private void QueueRadial(IReadOnlyList<string> files)
    {
        _pendingRadialFiles.AddRange(files);
        if (_radialDebounce is null)
        {
            _radialDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _radialDebounce.Tick += async (_, _) =>
            {
                _radialDebounce.Stop();
                var batch = _pendingRadialFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                _pendingRadialFiles.Clear();
                await _services!.GetRequiredService<RadialMenuService>().ShowAsync(batch);
            };
        }
        _radialDebounce.Stop();
        _radialDebounce.Start();
    }

    // IMainWindowHost
    public void ShowMain()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(_services!.GetRequiredService<MainViewModel>(), _services!.GetRequiredService<ThemeManager>());
            _mainWindow.Closing += OnMainWindowClosing;
        }
        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }
        _mainWindow.Activate();
    }

    public void ShowConvert(IReadOnlyList<string> files, string? targetKey = null)
    {
        ShowMain();
        _mainWindow!.ViewModel.ShowConvert(files, targetKey);
    }

    public void ShowHistory()
    {
        ShowMain();
        _mainWindow!.ViewModel.Navigate(Page.History);
    }

    private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting)
        {
            return;
        }
        if (_services!.GetRequiredService<ISettingsStore>().Current.RunInBackgroundWhenClosed)
        {
            e.Cancel = true;
            _mainWindow!.Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _services!.GetRequiredService<NotificationService>().Show("OrbitConvert is still running",
                    "Quick conversion stays available. Right-click the tray icon to exit.", null);
            }
            return;
        }
        e.Cancel = true;
        Dispatcher.BeginInvoke(ExitApplication);
    }

    private void ExitApplication()
    {
        var queue = _services!.GetRequiredService<ConversionQueue>();
        var active = queue.ActiveCount;
        if (active > 0 && MessageBox.Show($"{active} conversion(s) are still running or waiting. Exit and cancel them?", "OrbitConvert",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }
        _exiting = true;
        queue.CancelAll();
        _mainWindow?.Close();
        Shutdown();
    }

    private void RunShellRegistration(string command)
    {
        var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "OrbitConvert.exe");
        try
        {
            if (command == StartupArguments.RegisterShell)
            {
                ShellRegistration.RegisterContextMenu(exe);
                ShellRegistration.RegisterSendTo(exe);
            }
            else
            {
                ShellRegistration.UnregisterContextMenu();
                ShellRegistration.UnregisterSendTo();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Shell registration failed");
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogCritical(e.Exception, "Unhandled UI exception");
        MessageBox.Show("Something went wrong: " + ErrorTranslator.ToUserMessage(e.Exception) + "\n\nDetails were written to the log file.",
            "OrbitConvert", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("OrbitConvert exiting");
        if (_services is not null)
        {
            _services.GetService<ConversionQueue>()?.Dispose();
            _services.Dispose(); // disposes tray icon, hotkey, hooks, drop zone
        }
        _instance?.Dispose();
        base.OnExit(e);
    }
}

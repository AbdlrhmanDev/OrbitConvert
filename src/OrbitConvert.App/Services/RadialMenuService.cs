using Microsoft.Extensions.Logging;
using OrbitConvert.App.ViewModels;
using OrbitConvert.App.Views;
using OrbitConvert.Core.Settings;
using OrbitConvert.Shell;

namespace OrbitConvert.App.Services;

/// <summary>Opens the main window on a page; implemented by the application shell.</summary>
public interface IMainWindowHost
{
    void ShowMain();
    void ShowConvert(IReadOnlyList<string> files, string? targetKey = null);
    void ShowHistory();
}

/// <summary>Shows the radial conversion menu for a set of files at a screen position.</summary>
public sealed class RadialMenuService(
    ConversionCoordinator coordinator, ISettingsStore settings, Lazy<IMainWindowHost> host, ILogger<RadialMenuService> logger)
{
    private RadialMenuWindow? _current;

    /// <summary>True while a radial menu shows progress or a result, so a duplicate toast is not needed.</summary>
    public bool IsShowingResult =>
        _current is { IsVisible: true } w && !w.ViewModel.IsChoosing;

    public async Task ShowAsync(IReadOnlyList<string> paths, PixelPoint? anchor = null)
    {
        var files = paths.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
        {
            host.Value.ShowMain();
            return;
        }

        var point = anchor ?? ScreenGeometry.CursorPosition();
        _current?.FadeOutAndClose();

        var vm = new RadialMenuViewModel(files, coordinator, settings);
        vm.OpenMainRequested += (_, request) =>
        {
            if (request.TargetKey == "history")
            {
                host.Value.ShowHistory();
            }
            else
            {
                host.Value.ShowConvert(request.Files, request.TargetKey);
            }
        };

        try
        {
            await vm.LoadAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not analyze files for the radial menu");
            host.Value.ShowConvert(files);
            return;
        }

        var window = new RadialMenuWindow(vm, point);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_current, window))
            {
                _current = null;
            }
        };
        _current = window;
        window.Show();
        logger.LogInformation("Radial menu shown for {Count} file(s) with {Targets} target(s)", files.Count, vm.Items.Count - 1);
    }
}

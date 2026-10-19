using Microsoft.Extensions.Logging;
using OrbitConvert.App.ViewModels;
using OrbitConvert.App.Views;
using OrbitConvert.Core.Images;
using OrbitConvert.Shell;

namespace OrbitConvert.App.Services;

/// <summary>Creates the compact image utility windows and chains their outputs.</summary>
public sealed class ImageEditorService(
    IImageEditor editor,
    NotificationService notifications,
    Lazy<RadialMenuService> radial,
    ILogger<ImageEditorService> logger)
{
    public void OpenCrop(string path)
    {
        try
        {
            var vm = new CropEditorViewModel(path, editor);
            var window = new CropEditorWindow(vm);
            vm.CloseRequested += (_, _) => window.Close();
            vm.Saved += (_, result) =>
            {
                window.Close();
                notifications.Show("Cropped", Path.GetFileName(result.Path) + " · original kept", result.Path);
                // As in the desktop workflow: the Orbit Menu reopens on the new file with its actions.
                _ = radial.Value.ShowAsync([result.Path]);
            };
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not open crop editor for {Path}", path);
            notifications.Show("Could not open crop editor", Core.Conversion.ErrorTranslator.ToUserMessage(ex), null);
        }
    }

    public void OpenBackground(string path)
    {
        try
        {
            var vm = new BackgroundEditorViewModel(path, editor);
            var window = new BackgroundEditorWindow(vm);
            vm.CloseRequested += (_, _) => window.Close();
            vm.Saved += (_, result) =>
            {
                window.Close();
                notifications.Show("Saved " + Path.GetFileName(result.Path), "Original kept", result.Path);
                ShellLauncher.RevealInExplorer(result.Path);
            };
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not open background editor for {Path}", path);
            notifications.Show("Could not open background editor", Core.Conversion.ErrorTranslator.ToUserMessage(ex), null);
        }
    }

    public void OpenEditor(string path, string mode = "edit")
    {
        try
        {
            var vm = new ImageEditViewModel(path, editor, mode);
            var window = new ImageEditWindow(vm);
            vm.CloseRequested += (_, _) => window.Close();
            vm.Saved += (_, result) =>
            {
                window.Close();
                notifications.Show("Saved " + Path.GetFileName(result.Path), "Edits flattened · original kept", result.Path);
                ShellLauncher.RevealInExplorer(result.Path);
            };
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not open image editor for {Path}", path);
            notifications.Show("Could not open image editor", Core.Conversion.ErrorTranslator.ToUserMessage(ex), null);
        }
    }
}

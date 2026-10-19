using Microsoft.Extensions.Logging;
using OrbitConvert.App.ViewModels;
using OrbitConvert.App.Views;
using OrbitConvert.Core.Pdf;
using OrbitConvert.Shell;

namespace OrbitConvert.App.Services;

public sealed class PdfEditorService(IPdfEditor editor, NotificationService notifications, ILogger<PdfEditorService> logger)
{
    public void OpenReorder(string path)
    {
        try
        {
            var vm = new PdfReorderViewModel(path, editor);
            var window = new PdfReorderWindow(vm);
            vm.CloseRequested += (_, _) => window.Close();
            vm.Saved += (_, result) =>
            {
                window.Close();
                notifications.Show("PDF saved", Path.GetFileName(result.Path) + " · original kept", result.Path);
                ShellLauncher.RevealInExplorer(result.Path);
            };
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not open PDF editor for {Path}", path);
            notifications.Show("Could not open PDF editor", Core.Conversion.ErrorTranslator.ToUserMessage(ex), null);
        }
    }
}

using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OrbitConvert.Shell;

/// <summary>
/// Per-user Explorer integration using documented registry verbs and the Send To folder.
/// Everything lives under HKEY_CURRENT_USER, so no administrator rights are needed and
/// nothing is loaded into Explorer's process.
/// </summary>
/// <remarks>
/// On Windows 11 a classic verb appears under "Show more options" (Shift+F10). Appearing in the
/// compact top-level menu requires an IExplorerCommand handler shipped in a signed MSIX/sparse
/// package, which this project does not have.
/// </remarks>
public static class ShellRegistration
{
    public const string VerbKey = @"Software\Classes\*\shell\OrbitConvert";
    public const string MenuText = "Convert with OrbitConvert";
    public const string SendToName = "OrbitConvert.lnk";

    public static string SendToPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), SendToName);

    public static bool IsContextMenuRegistered(string executable)
    {
        using var command = Registry.CurrentUser.OpenSubKey(VerbKey + @"\command");
        return command?.GetValue(null) is string value && value.Contains(executable, StringComparison.OrdinalIgnoreCase);
    }

    public static void RegisterContextMenu(string executable)
    {
        using var verb = Registry.CurrentUser.CreateSubKey(VerbKey);
        verb.SetValue(null, MenuText);
        verb.SetValue("MUIVerb", MenuText);
        verb.SetValue("Icon", $"\"{executable}\",0");
        // "Player" lifts Explorer's 15-item limit for multi-selection. Explorer starts one process
        // per selected file; the running instance gathers them into a single radial menu.
        verb.SetValue("MultiSelectModel", "Player");
        using var command = verb.CreateSubKey("command");
        command.SetValue(null, $"\"{executable}\" --radial \"%1\"");
        NotifyAssociationsChanged();
    }

    public static void UnregisterContextMenu()
    {
        Registry.CurrentUser.DeleteSubKeyTree(VerbKey, throwOnMissingSubKey: false);
        NotifyAssociationsChanged();
    }

    public static bool IsSendToRegistered() => File.Exists(SendToPath);

    /// <summary>"Send to → OrbitConvert" passes all selected files to one process in a single call.</summary>
    public static void RegisterSendTo(string executable)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(SendToPath);
            link.TargetPath = executable;
            link.Arguments = "--radial";
            link.IconLocation = executable + ",0";
            link.Description = MenuText;
            link.WorkingDirectory = Path.GetDirectoryName(executable);
            link.Save();
            Marshal.FinalReleaseComObject(link);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    public static void UnregisterSendTo()
    {
        if (File.Exists(SendToPath))
        {
            File.Delete(SendToPath);
        }
    }

    private static void NotifyAssociationsChanged() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, 0, 0);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}

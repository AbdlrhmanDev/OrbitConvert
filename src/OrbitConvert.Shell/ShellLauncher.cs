using System.Diagnostics;

namespace OrbitConvert.Shell;

/// <summary>Opens files and folders through Explorer with properly quoted arguments.</summary>
public static class ShellLauncher
{
    /// <summary>Opens the containing folder with the item selected.</summary>
    public static void RevealInExplorer(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", Path.GetFullPath(path) }, UseShellExecute = false });
        }
        else if (Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir))
        {
            OpenFolder(dir);
        }
    }

    public static void OpenFolder(string folder)
    {
        if (Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Path.GetFullPath(folder) }, UseShellExecute = false });
        }
    }

    /// <summary>Opens an https link in the default browser. Other schemes are refused.</summary>
    public static void OpenUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }
}

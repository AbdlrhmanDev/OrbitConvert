using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OrbitConvert.Shell.Interop;

namespace OrbitConvert.Shell;

/// <summary>
/// Reads the files selected in the foreground File Explorer window using the documented
/// Shell.Application automation object (IShellWindows / IShellFolderViewDual). Nothing is
/// injected into Explorer. On Windows 11, where one Explorer window hosts several tabs that share
/// a window handle, the active tab is identified through IShellBrowser::GetWindow.
/// </summary>
public sealed class ExplorerSelection(ILogger<ExplorerSelection> logger)
{
    private static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IidShellBrowser = new("000214E2-0000-0000-C000-000000000046");

    /// <summary>Result of a selection query.</summary>
    public sealed record Result(IReadOnlyList<string> Files, string? Problem);

    /// <summary>Queries on a dedicated STA thread with a timeout, so a busy Explorer cannot freeze OrbitConvert.</summary>
    public Task<Result> GetSelectedFilesAsync(nint foregroundWindow, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.TrySetResult(Query(foregroundWindow));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Explorer selection query failed");
                tcs.TrySetResult(new Result([], "Could not read the selection from File Explorer."));
            }
        })
        { IsBackground = true, Name = "Explorer selection" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task.WaitAsync(timeout).ContinueWith(t =>
            t.IsCompletedSuccessfully ? t.Result : new Result([], "File Explorer did not respond."), TaskScheduler.Default);
    }

    private Result Query(nint foreground)
    {
        var root = NativeMethods.GetAncestor(foreground, 2 /* GA_ROOT */);
        var className = NativeMethods.ClassNameOf(root);
        if (className is "Progman" or "WorkerW")
        {
            return new Result([], "Desktop selections are not supported. Select files in a File Explorer window.");
        }
        if (className is not ("CabinetWClass" or "ExploreWClass"))
        {
            return new Result([], "Select files in File Explorer first.");
        }

        var shellType = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Shell.Application is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            var activeTab = NativeMethods.FindWindowEx(root, 0, "ShellTabWindowClass", null);
            dynamic windows = shell.Windows();
            List<string>? fallback = null;
            foreach (dynamic window in windows)
            {
                try
                {
                    if (window is null || new nint((long)window.HWND) != root)
                    {
                        continue;
                    }
                    var files = SelectedPaths(window);
                    if (activeTab == 0 || TabWindowOf(window) == activeTab)
                    {
                        return new Result(files, files.Count == 0 ? "No files are selected in File Explorer." : null);
                    }
                    fallback ??= files;
                }
                finally
                {
                    if (window is not null)
                    {
                        Marshal.FinalReleaseComObject(window);
                    }
                }
            }
            if (fallback is not null)
            {
                return new Result(fallback, fallback.Count == 0 ? "No files are selected in File Explorer." : null);
            }
            return new Result([], "Select files in File Explorer first.");
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static List<string> SelectedPaths(dynamic window)
    {
        var result = new List<string>();
        dynamic? document = window.Document;
        if (document is null)
        {
            return result;
        }
        dynamic items = document.SelectedItems();
        foreach (dynamic item in items)
        {
            // Skip folders and virtual items (Control Panel, libraries) that have no file path.
            if ((bool)item.IsFileSystem && !(bool)item.IsFolder && item.Path is string path && File.Exists(path))
            {
                result.Add(path);
            }
        }
        return result;
    }

    private static nint TabWindowOf(object window)
    {
        if (window is not IServiceProvider provider)
        {
            return 0;
        }
        var sid = SidTopLevelBrowser;
        var iid = IidShellBrowser;
        if (provider.QueryService(ref sid, ref iid, out var ptr) != 0 || ptr == 0)
        {
            return 0;
        }
        try
        {
            var browser = (IOleWindow)Marshal.GetObjectForIUnknown(ptr);
            return browser.GetWindow(out var hwnd) == 0 ? hwnd : 0;
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, out nint ppvObject);
    }

    /// <summary>IShellBrowser derives from IOleWindow; only its first method (GetWindow) is used.</summary>
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleWindow
    {
        [PreserveSig]
        int GetWindow(out nint phwnd);

        [PreserveSig]
        int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enterMode);
    }
}

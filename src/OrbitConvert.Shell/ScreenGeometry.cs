using OrbitConvert.Shell.Interop;

namespace OrbitConvert.Shell;

public readonly record struct PixelPoint(int X, int Y);

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>Monitor information in physical pixels plus the monitor's effective DPI.</summary>
public readonly record struct MonitorInfo(PixelRect Bounds, PixelRect WorkArea, double DpiScale);

/// <summary>
/// Physical-pixel cursor and monitor queries. Working in physical pixels and converting with the
/// target monitor's own DPI keeps placement correct on mixed-DPI multi-monitor setups.
/// </summary>
public static class ScreenGeometry
{
    public static PixelPoint CursorPosition()
    {
        NativeMethods.GetCursorPos(out var p);
        return new PixelPoint(p.X, p.Y);
    }

    /// <summary>Global (not per-window) state of the primary mouse button, valid during another app's drag.</summary>
    public static bool IsLeftButtonDown() => (NativeMethods.GetAsyncKeyState(0x01 /* VK_LBUTTON */) & 0x8000) != 0;

    public static nint ForegroundWindow() => NativeMethods.GetForegroundWindow();

    public static MonitorInfo MonitorAt(PixelPoint point)
    {
        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT { X = point.X, Y = point.Y }, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);
        var scale = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
        return new MonitorInfo(ToRect(info.rcMonitor), ToRect(info.rcWork), scale);
    }

    /// <summary>
    /// Top-left position (physical pixels) for a window of <paramref name="width"/>×<paramref name="height"/>
    /// physical pixels centered on <paramref name="anchor"/>, clamped inside the work area.
    /// </summary>
    public static PixelPoint CenterClamped(PixelPoint anchor, int width, int height, PixelRect workArea)
    {
        var x = anchor.X - width / 2;
        var y = anchor.Y - height / 2;
        x = Math.Clamp(x, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        y = Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
        return new PixelPoint(x, y);
    }

    /// <summary>Moves a window to a physical-pixel position without resizing or activating it.</summary>
    public static void MoveWindow(nint hwnd, PixelPoint topLeft) =>
        NativeMethods.SetWindowPos(hwnd, 0, topLeft.X, topLeft.Y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

    private static PixelRect ToRect(NativeMethods.RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);
}

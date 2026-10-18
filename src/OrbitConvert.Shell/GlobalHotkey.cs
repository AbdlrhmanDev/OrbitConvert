using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using OrbitConvert.Core.Settings;
using OrbitConvert.Shell.Interop;

namespace OrbitConvert.Shell;

/// <summary>
/// A system-wide shortcut registered with RegisterHotKey (the supported Win32 API; no keyboard hook).
/// Must be created on a thread with a message loop (the WPF UI thread).
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0x4F43; // "OC"

    private readonly HwndSource _source;
    private bool _registered;

    public GlobalHotkey()
    {
        // A message-only window receives WM_HOTKEY without appearing anywhere.
        var parameters = new HwndSourceParameters("OrbitConvertHotkey") { ParentWindow = new nint(-3), Width = 0, Height = 0, WindowStyle = 0 };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    public string? LastError { get; private set; }

    /// <summary>Registers the shortcut, replacing any previous one. Returns false if another app owns it.</summary>
    public bool Register(HotkeySettings settings)
    {
        Unregister();
        LastError = null;
        if (!TryParseKey(settings.Key, out var key))
        {
            LastError = $"\"{settings.Key}\" is not a valid key.";
            return false;
        }
        if (settings.Modifiers == HotkeyModifiers.None)
        {
            LastError = "Choose at least one modifier key (Ctrl, Alt, Shift or Win).";
            return false;
        }

        uint modifiers = NativeMethods.MOD_NOREPEAT;
        if (settings.Modifiers.HasFlag(HotkeyModifiers.Alt)) modifiers |= NativeMethods.MOD_ALT;
        if (settings.Modifiers.HasFlag(HotkeyModifiers.Control)) modifiers |= NativeMethods.MOD_CONTROL;
        if (settings.Modifiers.HasFlag(HotkeyModifiers.Shift)) modifiers |= NativeMethods.MOD_SHIFT;
        if (settings.Modifiers.HasFlag(HotkeyModifiers.Windows)) modifiers |= NativeMethods.MOD_WIN;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _registered = NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, modifiers, vk);
        if (!_registered)
        {
            LastError = Marshal.GetLastPInvokeError() == 1409
                ? $"{settings} is already used by another application."
                : $"Could not register {settings} (error {Marshal.GetLastPInvokeError()}).";
        }
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
    }

    public static bool TryParseKey(string? text, out Key key)
    {
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        var t = text.Trim();
        if (t.Length == 1 && char.IsDigit(t[0]))
        {
            t = "D" + t;
        }
        return Enum.TryParse(t, ignoreCase: true, out key) && key is not Key.None
            && key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return 0;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}

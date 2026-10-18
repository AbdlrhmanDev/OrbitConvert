using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OrbitConvert.Shell.Interop;

namespace OrbitConvert.Shell;

/// <summary>
/// EXPERIMENTAL, off by default. Detects a quick left-right "shake" of the mouse while the left
/// button is held (as when dragging files) and raises <see cref="Shaken"/> so a drop target can
/// appear under the cursor.
/// </summary>
/// <remarks>
/// Uses a low-level mouse hook (WH_MOUSE_LL). Such hooks run in OrbitConvert's own process — no code
/// is injected into Explorer — and only observe pointer coordinates. Windows cannot tell an
/// application that a drag of files is in progress, so this cannot distinguish a file drag from
/// any other left-button drag; the drop target simply ignores anything that is not a file drop.
/// The callback is kept minimal because Windows silently removes hooks that respond slowly.
/// </remarks>
public sealed class DragShakeDetector : IDisposable
{
    private const int MinSwing = 40;          // pixels a stroke must travel to count
    private const int RequiredReversals = 4;  // direction changes that make a "shake"
    private const uint WindowMs = 900;

    private readonly ILogger _logger;
    private readonly NativeMethods.LowLevelMouseProc _proc; // keep the delegate alive
    private nint _hook;

    private bool _buttonDown;
    private bool _firedThisDrag;
    private int _anchorX;
    private int _direction;
    private readonly Queue<uint> _reversals = new();

    public DragShakeDetector(ILogger logger)
    {
        _logger = logger;
        _proc = HookProc;
    }

    /// <summary>Raised on the hook thread (the UI thread that called <see cref="Start"/>).</summary>
    public event EventHandler<PixelPoint>? Shaken;

    public bool IsRunning => _hook != 0;

    public bool Start()
    {
        if (_hook != 0)
        {
            return true;
        }
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == 0)
        {
            _logger.LogWarning("Could not install mouse hook: {Error}", Marshal.GetLastPInvokeError());
        }
        return _hook != 0;
    }

    public void Stop()
    {
        if (_hook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    private nint HookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            switch ((int)wParam)
            {
                case NativeMethods.WM_LBUTTONDOWN:
                    _buttonDown = true;
                    _firedThisDrag = false;
                    _anchorX = data.pt.X;
                    _direction = 0;
                    _reversals.Clear();
                    break;
                case NativeMethods.WM_LBUTTONUP:
                    _buttonDown = false;
                    break;
                case NativeMethods.WM_MOUSEMOVE when _buttonDown && !_firedThisDrag:
                    Track(data.pt.X, data.pt.Y, data.time);
                    break;
            }
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    internal void Track(int x, int y, uint time)
    {
        var delta = x - _anchorX;
        if (Math.Abs(delta) < MinSwing)
        {
            return;
        }
        var direction = Math.Sign(delta);
        if (_direction != 0 && direction != _direction)
        {
            _reversals.Enqueue(time);
            while (_reversals.Count > 0 && time - _reversals.Peek() > WindowMs)
            {
                _reversals.Dequeue();
            }
            if (_reversals.Count >= RequiredReversals)
            {
                _firedThisDrag = true;
                _reversals.Clear();
                Shaken?.Invoke(this, new PixelPoint(x, y));
            }
        }
        _direction = direction;
        _anchorX = x;
    }

    public void Dispose() => Stop();
}

using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace BetterWorkTime.App;

/// <summary>
/// Registers system-wide hotkeys and fires events when they are pressed.
/// Must be created and disposed on the UI thread.
/// </summary>
public sealed class GlobalHotkeyManager : IDisposable
{
    private const int WM_HOTKEY      = 0x0312;
    private const int MOD_ALT        = 0x0001;
    private const int MOD_CONTROL    = 0x0002;
    private const int MOD_ALT_CTRL   = MOD_ALT | MOD_CONTROL;

    private const int IdStartStop    = 1;
    private const int IdSwitchTask   = 2;
    private const int IdOpenMain     = 3;
    private const int IdAddNote      = 4;

    // Virtual key codes
    private const int VK_S = 0x53;
    private const int VK_T = 0x54;
    private const int VK_O = 0x4F;
    private const int VK_N = 0x4E;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public event Action? StartStopPressed;
    public event Action? SwitchTaskPressed;
    public event Action? OpenMainPressed;
    public event Action? AddNotePressed;

    private HwndSource? _source;
    private bool _registered;

    /// <summary>
    /// Registers Ctrl+Alt+S, Ctrl+Alt+T, Ctrl+Alt+O, Ctrl+Alt+N.
    /// Call only when hotkeys are enabled in settings.
    /// </summary>
    public void Register()
    {
        if (_registered) return;

        var p = new HwndSourceParameters("BwtHotkeyWindow")
        {
            Width = 0, Height = 0, WindowStyle = 0
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);

        var hwnd = _source.Handle;
        RegisterHotKey(hwnd, IdStartStop,  MOD_ALT_CTRL, VK_S);
        RegisterHotKey(hwnd, IdSwitchTask, MOD_ALT_CTRL, VK_T);
        RegisterHotKey(hwnd, IdOpenMain,   MOD_ALT_CTRL, VK_O);
        RegisterHotKey(hwnd, IdAddNote,    MOD_ALT_CTRL, VK_N);

        _registered = true;
    }

    public void Unregister()
    {
        if (!_registered || _source == null) return;
        var hwnd = _source.Handle;
        UnregisterHotKey(hwnd, IdStartStop);
        UnregisterHotKey(hwnd, IdSwitchTask);
        UnregisterHotKey(hwnd, IdOpenMain);
        UnregisterHotKey(hwnd, IdAddNote);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam,
        ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            switch (wParam.ToInt32())
            {
                case IdStartStop:  StartStopPressed?.Invoke();  handled = true; break;
                case IdSwitchTask: SwitchTaskPressed?.Invoke(); handled = true; break;
                case IdOpenMain:   OpenMainPressed?.Invoke();   handled = true; break;
                case IdAddNote:    AddNotePressed?.Invoke();    handled = true; break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source?.Dispose();
        _source = null;
    }
}

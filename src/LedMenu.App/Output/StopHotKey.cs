using System.Windows.Interop;
using LedMenu.Core.Logging;
using static LedMenu.App.Display.NativeMethods;

namespace LedMenu.App.Output;

/// <summary>
/// System-wide Ctrl+Shift+F12, registered only while output is running. It works even when the LED
/// output window covers the operator window and nothing in this application has keyboard focus.
/// </summary>
public sealed class StopHotKey : IDisposable
{
    private const int Id = 0x4C44;
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly IAppLog _log;

    public StopHotKey(IntPtr operatorWindowHandle, IAppLog log)
    {
        _hwnd = operatorWindowHandle;
        _log = log;
        _source = HwndSource.FromHwnd(operatorWindowHandle)
                  ?? throw new InvalidOperationException("Operator window handle is not ready.");
        _source.AddHook(Hook);
    }

    public bool IsRegistered { get; private set; }
    public event Action? Pressed;

    /// <summary>Returns false if another program already owns the shortcut; the in-window shortcut and button still work.</summary>
    public bool Register()
    {
        if (IsRegistered) return true;
        IsRegistered = RegisterHotKey(_hwnd, Id, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_F12);
        if (!IsRegistered) _log.Warn("Could not register the global Ctrl+Shift+F12 stop shortcut (already in use?).");
        return IsRegistered;
    }

    public void Unregister()
    {
        if (!IsRegistered) return;
        UnregisterHotKey(_hwnd, Id);
        IsRegistered = false;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == Id)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(Hook);
    }
}

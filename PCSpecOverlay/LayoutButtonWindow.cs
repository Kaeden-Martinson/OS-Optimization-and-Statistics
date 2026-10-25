using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace PCSpecOverlay;

/// <summary>
/// The click target for the overlay's Strip ⇄ Panel icon. The pinned overlay is click-through (WS_EX_TRANSPARENT), and
/// HTTRANSPARENT only passes clicks to windows of the same thread, so this tiny window of its own sits exactly over
/// the icon and catches clicks there; the overlay draws the icon. It is all but invisible (a 1/255 fill, which Windows
/// still hit-tests), stays above the overlay (its owner), never takes focus and stays out of the taskbar and Alt+Tab.
/// </summary>
public sealed class LayoutButtonWindow : Window
{
    private const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
    private const int WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203;
    private const int WM_CAPTURECHANGED = 0x215, WM_MOUSELEAVE = 0x2A3;

    private readonly Button _button;
    private (int Left, int Top, int Width, int Height)? _rect;
    private bool _pressed; // the left button went down inside the window and hasn't come up or left yet

    public IntPtr Handle { get; }

    /// <summary>Raised once per click (from the window's own messages) or screen-reader Invoke (from the Button).</summary>
    public event Action? Clicked;

    /// <summary>Raised with true when the pointer comes over the button and false when it leaves.</summary>
    public event Action<bool>? Hover;

    public LayoutButtonWindow(IntPtr owner)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        SizeToContent = SizeToContent.Manual;
        Width = Height = 16;

        // Alpha 0 would let clicks fall through to the game; 1/255 can't be seen but is still hit-tested
        var fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        fill.Freeze();
        var face = new FrameworkElementFactory(typeof(Border));
        face.SetValue(Border.BackgroundProperty, fill);
        _button = new Button
        {
            Template = new ControlTemplate(typeof(Button)) { VisualTree = face },
            Focusable = false,
            IsTabStop = false,
            Cursor = Cursors.Hand,
        };
        _button.Click += (_, _) => Clicked?.Invoke();
        _button.MouseEnter += (_, _) => Hover?.Invoke(true);
        _button.MouseLeave += (_, _) => Hover?.Invoke(false);
        Content = _button;
        SetLayout(Settings.Compact);

        var helper = new WindowInteropHelper(this) { Owner = owner };
        Handle = helper.EnsureHandle();
        var ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
        Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex & ~Native.WS_EX_TRANSPARENT);
        HwndSource.FromHwnd(Handle)?.AddHook(WndProc);
        // Moving onto a display with other scaling makes Windows suggest a rescaled size: keep the icon's rectangle
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(Reapply));
    }

    /// <summary>The screen-reader name says what a click does in the layout shown now.</summary>
    public void SetLayout(string shown)
    {
        var name = shown == Settings.Expanded ? "Switch to Strip" : "Switch to Panel";
        if (Title == name) return;
        Title = name;
        AutomationProperties.SetName(_button, name);
    }

    /// <summary>Puts the window over this rectangle (physical pixels) and shows it; moves it only when the rectangle changed.</summary>
    public void Place((int Left, int Top, int Width, int Height) rect)
    {
        if (_rect != rect)
        {
            _rect = rect;
            Reapply();
        }
        if (!IsVisible)
        {
            Show();
            KeepOnTop();
        }
    }

    private void Reapply()
    {
        if (_rect is not { } r) return;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, Native.SWP_NOACTIVATE);
    }

    /// <summary>Above the overlay: re-asserted with the overlay's own z-order.</summary>
    public void KeepOnTop()
    {
        if (IsVisible)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>
    /// A click never activates the window, so the game keeps focus. Clicks are taken from the window's own messages
    /// rather than WPF's Button, which also checks where the pointer is and could miss one; the button-down and -up
    /// messages are handled here so WPF never sees them and Button.Click is left to UI Automation Invoke: one click,
    /// one Clicked. An up counts only after a down inside the window, and leaving or losing capture cancels the press.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE:
                handled = true;
                return MA_NOACTIVATE;
            case WM_LBUTTONDOWN or WM_LBUTTONDBLCLK:
                _pressed = Inside(lParam);
                handled = true;
                break;
            case WM_LBUTTONUP:
                var click = _pressed && Inside(lParam);
                _pressed = false;
                handled = true;
                if (click) Clicked?.Invoke();
                break;
            case WM_MOUSELEAVE or WM_CAPTURECHANGED:
                _pressed = false; // not handled: WPF needs these too
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>Whether a mouse message's client coordinates fall inside the window.</summary>
    private bool Inside(IntPtr lParam)
    {
        var v = (int)(long)lParam;
        int x = (short)(v & 0xFFFF), y = (short)((v >> 16) & 0xFFFF);
        return Native.GetClientRect(Handle, out var c) && x >= 0 && y >= 0 && x < c.Right && y < c.Bottom;
    }
}

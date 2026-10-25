using System.Drawing;
using Microsoft.Win32;
using PCSpecOverlay.Core;
using Forms = System.Windows.Forms;
using Icon = System.Drawing.Icon;

namespace PCSpecOverlay;

/// <summary>Notification-area icon and quick menu. Everything else lives in the control center.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly App _app;
    private readonly Forms.NotifyIcon _icon;
    private Icon _image;
    private bool _imageLight;
    private bool _disposed;

    /// <summary>Reserves room at the right of a switch-like item for its check (the renderer draws nothing for it).</summary>
    private const string CheckRoom = "  ";

    private readonly Forms.ContextMenuStrip _menu;
    private readonly List<(Forms.ToolStripMenuItem Item, string Icon)> _iconItems = new();
    private TrayPalette _palette = TrayPalette.Dark;
    private Image? _check;

    public TrayIcon(App app)
    {
        _app = app;
        _imageLight = TaskbarIsLight();
        _image = DrawIcon(_imageLight);

        _menu = new Forms.ContextMenuStrip { ShowImageMargin = true, ShowCheckMargin = false };
        _menu.Renderer = new TrayMenuRenderer(() => _palette, () => _check);

        Forms.ToolStripMenuItem Item(string text, string icon, Action click)
        {
            var item = new Forms.ToolStripMenuItem(text);
            item.Click += (_, _) => click();
            _iconItems.Add((item, icon));
            return item;
        }

        var center = Item($"Open {AppIdentity.ProductName}", "Icon.Window", _app.OpenControlCenter);
        center.Font = new Font(_menu.Font, FontStyle.Bold);
        var overlay = Item("Hide overlay", "Icon.Overlay", _app.ToggleOverlay);
        var move = Item("Move overlay", "Icon.Pointer", _app.MoveOverlay);
        var gaming = Item("Automatic gaming mode", "Icon.Gaming", () =>
        {
            _app.Settings.AutoGamingMode = !_app.Settings.AutoGamingMode;
            _app.SaveSettings();
        });
        gaming.ShortcutKeyDisplayString = CheckRoom;
        var pause = Item("Stop optimizations now", "Icon.Pause", () =>
        {
            if (_app.Engine.OptimizationsPaused) _app.ResumeOptimizations();
            else Notify(string.Join(" ", _app.StopOptimizations()), "Optimizations stopped");
        });
        Forms.ToolStripMenuItem startup = null!;
        startup = Item("Start with Windows", "Icon.Monitor", () =>
        {
            var result = StartupTask.SetEnabled(!startup.Checked);
            if (result != null) Notify(result, "Start with Windows");
        });
        startup.ShortcutKeyDisplayString = CheckRoom;
        var exit = Item("Exit", "Icon.Power", () => _app.Shutdown());

        _menu.Items.AddRange([center, new Forms.ToolStripSeparator(), overlay, move, gaming, pause, startup,
                              new Forms.ToolStripSeparator(), exit]);
        _menu.Opening += (_, _) =>
        {
            overlay.Text = _app.OverlayVisible ? "Hide overlay" : "Show overlay";
            overlay.ShortcutKeyDisplayString = _app.HotkeyInUse;
            gaming.Checked = _app.Settings.AutoGamingMode;
            pause.Text = _app.Engine.OptimizationsPaused ? "Resume optimizations" : "Stop optimizations now";
            startup.Checked = StartupTask.Status().Enabled;
        };
        ApplyTheme();
        _app.Theme.Changed += OnAppThemeChanged;

        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = AppIdentity.ProductName,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) _app.OpenControlCenter();
        };
        _icon.BalloonTipClicked += (_, _) => _app.OpenControlCenter();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Colors and icons follow the app's light or dark theme; redrawn whenever it changes.</summary>
    private void ApplyTheme()
    {
        _palette = TrayPalette.From(key => _app.TryFindResource(key) is System.Windows.Media.Color c ? c : null);
        var size = (int)Math.Round(16 * _menu.DeviceDpi / 96.0);
        _menu.ImageScalingSize = new Size(size, size);
        var ink = System.Windows.Media.Color.FromArgb(_palette.Text.A, _palette.Text.R, _palette.Text.G, _palette.Text.B);
        foreach (var (item, key) in _iconItems)
        {
            var old = item.Image;
            item.Image = _app.TryFindResource(key) is System.Windows.Media.Geometry g ? MenuIcons.Render(g, size, ink) : null;
            old?.Dispose();
        }
        var oldCheck = _check;
        _check = _app.TryFindResource("Icon.Check") is System.Windows.Media.Geometry check ? MenuIcons.Render(check, size, ink) : null;
        oldCheck?.Dispose();
        _menu.Invalidate();
    }

    private void OnAppThemeChanged(AppTheme theme)
    {
        if (!_disposed) ApplyTheme();
    }

    /// <param name="important">Warning icon for things that need attention; info icon otherwise.</param>
    public void Notify(string message, string title = AppIdentity.ProductName, bool important = false)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        _icon.ShowBalloonTip(6000, title, message, important ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
    }

    /// <summary>The Optiframe mark at the tray size: dark on a light taskbar, light on a dark one.</summary>
    private static Icon DrawIcon(bool light)
    {
        var size = Forms.SystemInformation.SmallIconSize.Width;
        using var bmp = LogoPainter.Draw(size, light ? Color.FromArgb(0x14, 0x14, 0x14) : Color.FromArgb(0xE9, 0xE9, 0xE6));
        var handle = bmp.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle); // doesn't own the handle
            return (Icon)borrowed.Clone();
        }
        finally
        {
            Native.DestroyIcon(handle);
        }
    }

    private static bool TaskbarIsLight() => ThemeResolver.TaskbarIsLight(ThemeResolver.ReadSystemUsesLightTheme);

    /// <summary>General fires for many unrelated changes; redraw only when the taskbar's light/dark flips.</summary>
    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        _app.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            var light = TaskbarIsLight();
            if (light == _imageLight) return;
            var old = _image;
            _image = DrawIcon(light);
            _imageLight = light;
            _icon.Icon = _image;
            old.Dispose();
        });
    }

    public void Dispose()
    {
        _disposed = true;
        _app.Theme.Changed -= OnAppThemeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        foreach (var (item, _) in _iconItems) item.Image?.Dispose();
        _check?.Dispose();
        _image.Dispose();
    }
}

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

    public TrayIcon(App app)
    {
        _app = app;
        _image = DrawIcon();

        var menu = new Forms.ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };

        var center = new Forms.ToolStripMenuItem("Open control center") { Font = new Font(menu.Font, FontStyle.Bold) };
        center.Click += (_, _) => _app.OpenControlCenter();

        var overlay = new Forms.ToolStripMenuItem("Show overlay");
        overlay.Click += (_, _) => _app.ToggleOverlay();

        var move = new Forms.ToolStripMenuItem("Move overlay");
        move.Click += (_, _) => _app.MoveOverlay();

        var gaming = new Forms.ToolStripMenuItem("Automatic gaming mode");
        gaming.Click += (_, _) =>
        {
            _app.Settings.AutoGamingMode = !_app.Settings.AutoGamingMode;
            _app.SaveSettings();
        };

        var pause = new Forms.ToolStripMenuItem("Stop optimizations now");
        pause.Click += (_, _) =>
        {
            if (_app.Engine.OptimizationsPaused) _app.ResumeOptimizations();
            else Notify(string.Join(" ", _app.StopOptimizations()), "Optimizations stopped");
        };

        var startup = new Forms.ToolStripMenuItem("Start with Windows");
        startup.Click += (_, _) =>
        {
            var result = StartupTask.SetEnabled(!startup.Checked);
            if (result != null) Notify(result, "Start with Windows");
        };

        var exit = new Forms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => _app.Shutdown();

        menu.Items.AddRange([center, new Forms.ToolStripSeparator(), overlay, move, gaming, pause, startup,
                             new Forms.ToolStripSeparator(), exit]);
        menu.Opening += (_, _) =>
        {
            overlay.Checked = _app.OverlayVisible;
            overlay.ShortcutKeyDisplayString = _app.HotkeyInUse;
            gaming.Checked = _app.Settings.AutoGamingMode;
            pause.Text = _app.Engine.OptimizationsPaused ? "Resume optimizations" : "Stop optimizations now";
            startup.Checked = StartupTask.Status().Enabled;
        };

        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = AppIdentity.ProductName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) _app.OpenControlCenter();
        };
        _icon.BalloonTipClicked += (_, _) => _app.OpenControlCenter();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <param name="important">Warning icon for things that need attention; info icon otherwise.</param>
    public void Notify(string message, string title = AppIdentity.ProductName, bool important = false)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        _icon.ShowBalloonTip(6000, title, message, important ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
    }

    /// <summary>The Optiframe mark at the tray size: dark on a light taskbar, light on a dark one.</summary>
    private static Icon DrawIcon()
    {
        var size = Forms.SystemInformation.SmallIconSize.Width;
        var light = ThemeResolver.TaskbarIsLight(ThemeResolver.ReadSystemUsesLightTheme);
        using var bmp = LogoPainter.Draw(size, light ? Color.FromArgb(0x14, 0x14, 0x14) : Color.FromArgb(0xE9, 0xE9, 0xE6));
        return Icon.FromHandle(bmp.GetHicon());
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        _app.Dispatcher.BeginInvoke(() =>
        {
            var old = _image;
            _image = DrawIcon();
            _icon.Icon = _image;
            old.Dispose();
        });
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using PCSpecOverlay.Core;
using Forms = System.Windows.Forms;
using Icon = System.Drawing.Icon;

namespace PCSpecOverlay;

/// <summary>Notification-area icon and quick menu. Everything else lives in the control center.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly App _app;
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _image;

    public TrayIcon(App app)
    {
        _app = app;
        _image = DrawIcon();

        var menu = new Forms.ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };

        var center = new Forms.ToolStripMenuItem("Open control center") { Font = new Font(menu.Font, FontStyle.Bold) };
        center.Click += (_, _) => _app.OpenControlCenter();

        var overlay = new Forms.ToolStripMenuItem("Show overlay");
        overlay.Click += (_, _) => _app.ToggleOverlay();

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

        menu.Items.AddRange([center, new Forms.ToolStripSeparator(), overlay, gaming, pause, startup,
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
            Text = "PC Spec Overlay",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) _app.OpenControlCenter();
        };
        _icon.BalloonTipClicked += (_, _) => _app.OpenControlCenter();
    }

    /// <param name="important">Warning icon for things that need attention; info icon otherwise.</param>
    public void Notify(string message, string title = "PC Spec Overlay", bool important = false)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        _icon.ShowBalloonTip(6000, title, message, important ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
    }

    /// <summary>Three rising bars in the accent green.</summary>
    private static Icon DrawIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(118, 185, 0));
            g.FillRectangle(brush, 3, 19, 7, 10);
            g.FillRectangle(brush, 12, 11, 7, 18);
            g.FillRectangle(brush, 21, 3, 7, 26);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}

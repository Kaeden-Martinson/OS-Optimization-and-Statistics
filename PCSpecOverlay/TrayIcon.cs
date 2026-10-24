using System.Drawing;
using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace PCSpecOverlay;

/// <summary>Notification-area icon and menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private static readonly (string Key, string Label)[] Corners =
        [("TopRight", "Top right"), ("TopLeft", "Top left"), ("BottomRight", "Bottom right"), ("BottomLeft", "Bottom left")];

    private readonly App _app;
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _image;

    public TrayIcon(App app, string? hotkey)
    {
        _app = app;
        _image = DrawIcon();

        var menu = new Forms.ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };

        var toggle = new Forms.ToolStripMenuItem("Show overlay") { ShortcutKeyDisplayString = hotkey };
        toggle.Click += (_, _) => _app.ToggleOverlay();

        var position = new Forms.ToolStripMenuItem("Position");
        foreach (var (key, label) in Corners)
        {
            var item = new Forms.ToolStripMenuItem(label) { Tag = key };
            item.Click += (_, _) => _app.SetPosition(key);
            position.DropDownItems.Add(item);
        }

        var optimize = new Forms.ToolStripMenuItem("Optimize…") { Font = new Font(menu.Font, FontStyle.Bold) };
        optimize.Click += (_, _) => _app.OpenOptimizer();

        var boost = new Forms.ToolStripMenuItem("Game Boost");
        boost.Click += (_, _) => _app.SetGameBoost(!_app.GameBoostEnabled);

        var gaming = new Forms.ToolStripMenuItem("Automatic gaming mode");
        gaming.Click += (_, _) => _app.SetAutoGamingMode(!_app.AutoGamingModeEnabled);

        var alerts = new Forms.ToolStripMenuItem("Overheating alerts");
        alerts.Click += (_, _) => _app.SetAlerts(!_app.AlertsEnabled);

        var startup = new Forms.ToolStripMenuItem("Start with Windows");
        startup.Click += (_, _) =>
        {
            if (!StartupTask.SetEnabled(!startup.Checked))
                Notify("Couldn't change the startup task.");
        };

        var elevate = new Forms.ToolStripMenuItem("Restart as administrator (enables FPS)");
        elevate.Click += (_, _) => _app.RestartElevated();

        var settings = new Forms.ToolStripMenuItem("Edit settings…");
        settings.Click += (_, _) => _app.OpenSettingsFile();

        var exit = new Forms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => _app.Shutdown();

        menu.Items.AddRange([optimize, gaming, boost, new Forms.ToolStripSeparator(), toggle, position, alerts, startup,
                             new Forms.ToolStripSeparator(), elevate, settings, new Forms.ToolStripSeparator(), exit]);
        menu.Opening += (_, _) =>
        {
            toggle.Checked = _app.OverlayVisible;
            gaming.Checked = _app.AutoGamingModeEnabled;
            boost.Checked = _app.GameBoostEnabled;
            alerts.Checked = _app.AlertsEnabled;
            foreach (Forms.ToolStripMenuItem item in position.DropDownItems)
                item.Checked = (string)item.Tag! == _app.Position;
            startup.Checked = StartupTask.IsEnabled();
            elevate.Visible = !_app.FpsAvailable;
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
            if (e.Button == Forms.MouseButtons.Left) _app.ToggleOverlay();
        };
    }

    public void Notify(string message, string title = "PC Spec Overlay") =>
        _icon.ShowBalloonTip(6000, title, message, Forms.ToolTipIcon.Warning);

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

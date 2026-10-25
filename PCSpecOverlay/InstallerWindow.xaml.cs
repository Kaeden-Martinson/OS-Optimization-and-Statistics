using System.Windows;
using PCSpecOverlay.Core;

namespace PCSpecOverlay;

/// <summary>Setup / uninstall dialog. Nothing happens until the user presses the main button.</summary>
public partial class InstallerWindow : Window
{
    public enum Mode { Install, Uninstall }

    private readonly Mode _mode;
    private bool _done;
    private readonly ThemeManager _theme = new(Application.Current.Resources);

    public InstallerWindow(Mode mode)
    {
        InitializeComponent();
        _mode = mode;
        // Setup runs before the app has a theme, and can't read the installed copy's settings: follow Windows
        _theme.Apply(Settings.MatchWindows);
        SourceInitialized += (_, _) => TitleBar.Apply(this, _theme.Current == AppTheme.Dark);
        _theme.Changed += theme => TitleBar.Apply(this, theme == AppTheme.Dark);
        Closed += (_, _) => _theme.Dispose();

        if (mode == Mode.Install)
        {
            var installed = Installer.InstalledVersion();
            Heading.Text = installed is null ? $"Install {AppIdentity.ProductName}" : $"Update {AppIdentity.ProductName}";
            Intro.Text = installed is null
                ? $"Version {Installer.Version} will be installed for your account in {Installer.Describe(AppIdentity.InstallDirectory)}. " +
                  "No administrator rights are needed. You can remove it any time from Settings › Apps."
                : $"Version {installed} is installed; this will replace it with {Installer.Version}. Your settings are kept, " +
                  "and the previous version is kept as a fallback. A running copy will be closed first (it puts back any changes it made).";
            Option1.Content = "Start with Windows";
            Option1.IsChecked = installed is null || StartupTask.Status().Enabled;
            Option2Row.Visibility = Visibility.Collapsed;
            Primary.Content = installed is null ? "Install" : "Update";
        }
        else
        {
            Heading.Text = $"Uninstall {AppIdentity.ProductName}";
            Intro.Text = "This removes the app, its Start menu shortcut, its Apps entry and Start with Windows.";
            Option1.Content = "Undo Windows changes the app made (power mode, browser graphics settings, Game Mode, …) — recommended";
            Option2.Content = "Also delete my settings and gaming history";
            Primary.Content = "Uninstall";
        }
    }

    private void OnPrimary(object sender, RoutedEventArgs e)
    {
        if (_done) { Close(); return; }
        Primary.IsEnabled = Secondary.IsEnabled = false;
        Options.IsEnabled = false;

        List<string> report;
        bool ok;
        if (_mode == Mode.Install)
        {
            var error = Installer.Install(Option1.IsChecked == true);
            ok = error is null;
            report = ok ? [$"Installed. {AppIdentity.ProductName} is running — find it in the tray and in the Start menu."] : [error!];
        }
        else
        {
            report = Installer.Uninstall(Option1.IsChecked == true, Option2.IsChecked == true);
            ok = true;
        }

        ResultText.Text = string.Join(Environment.NewLine, report);
        ResultBox.Visibility = Visibility.Visible;
        _done = ok;
        Primary.Content = ok ? "Close" : "Try again";
        Primary.Tag = "Primary";
        Primary.IsEnabled = true;
        Secondary.IsEnabled = !ok;
        Options.IsEnabled = !ok;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using PCSpecOverlay.Optimizer;

namespace PCSpecOverlay;

public partial class OptimizerWindow : Window
{
    private const int RowCount = 8;

    // Closing these would log you out or crash Windows; most are protected anyway.
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "csrss", "winlogon", "wininit", "services", "lsass", "smss", "svchost", "Registry",
        "Memory Compression", "fontdrvhost", "sihost", "ctfmon", "audiodg", "System", "PCSpecOverlay",
    };

    private readonly App _app;
    private readonly Settings _settings;
    private readonly List<Check> _checks;
    private readonly ProcessSampler _sampler = new();
    private readonly ObservableCollection<AppRow> _rows = new();
    private readonly DispatcherTimer _timer;

    internal OptimizerWindow(App app, Settings settings)
    {
        InitializeComponent();
        // Center on the primary screen ourselves: CenterScreen misplaces the window across monitors with different scaling
        var area = SystemParameters.WorkArea;
        Height = Math.Min(Height, area.Height - 40);
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
        _app = app;
        _settings = settings;

        _checks = Checks.Create(settings);
        CheckList.ItemsSource = _checks;
        AppList.ItemsSource = _rows;
        BoostToggle.IsChecked = app.GameBoostEnabled; // raises Checked, which re-applies the same state
        GamingToggle.IsChecked = settings.AutoGamingMode;
        GamesBox.Text = string.Join(", ", settings.Games);
        UpdateGamingStatus();
        UpdateFixAll();
        UpdateBoostTargets();

        SourceInitialized += (_, _) =>
        {
            var dark = 1; // DWMWA_USE_IMMERSIVE_DARK_MODE
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Dispatcher.BeginInvoke(Tick, DispatcherPriority.Background);
        Closed += (_, _) => _timer.Stop();
    }

    private void Tick()
    {
        foreach (var c in _checks) c.Refresh();
        UpdateFixAll();
        UpdateGamingStatus();

        var top = _sampler.Sample()
            .OrderByDescending(a => Math.Max(a.CpuPercent, a.GpuPercent))
            .ThenByDescending(a => a.RamMb)
            .Take(RowCount)
            .ToList();

        // Update rows in place so buttons don't jump out from under the cursor
        for (var i = 0; i < top.Count; i++)
        {
            if (i >= _rows.Count) _rows.Add(new AppRow(this));
            _rows[i].Set(top[i], _settings.BoostTargets.Contains(top[i].Name, StringComparer.OrdinalIgnoreCase),
                         !Protected.Contains(top[i].Name));
        }
        while (_rows.Count > top.Count) _rows.RemoveAt(_rows.Count - 1);
    }

    private void UpdateFixAll()
    {
        FixAllButton.IsEnabled = _checks.OfType<Tweak>().Any(t => t.NeedsFix);
        UpdateRestartBanner();
    }

    private bool _restarting;

    private List<GpuPreferenceTweak> BrowsersNeedingRestart() =>
        _checks.OfType<GpuPreferenceTweak>().Where(b => b.NeedsRestart).ToList();

    private void UpdateRestartBanner()
    {
        if (_restarting) return;
        var browsers = BrowsersNeedingRestart();
        RestartBanner.Visibility = browsers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (browsers.Count == 0) return;

        var names = string.Join(" and ", browsers.Select(b => b.AppName));
        RestartTitle.Text = $"Restart {names} to switch graphics chips";
        RestartButton.Content = $"Restart {names}";
        RestartButton.IsEnabled = true;
    }

    private async void OnRestartBrowsers(object sender, RoutedEventArgs e)
    {
        var browsers = BrowsersNeedingRestart();
        if (browsers.Count == 0) return;
        var names = string.Join(" and ", browsers.Select(b => b.AppName));

        var answer = MessageBox.Show(this,
            $"{names} will close and reopen with all your windows and tabs.\n\nAnything unsaved on a page, like a form or a game in progress, will be lost.",
            $"Restart {names}", MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.OK);
        if (answer != MessageBoxResult.OK) return;

        _restarting = true;
        RestartButton.IsEnabled = false;
        RestartButton.Content = "Restarting…";
        try
        {
            foreach (var b in browsers) await BrowserRestart.RestartAsync(b.ExePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, $"Couldn't restart {names}", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _restarting = false;
            UpdateRestartBanner();
        }
    }

    private void UpdateBoostTargets() =>
        BoostTargetsText.Text = _settings.BoostTargets.Count == 0
            ? "Turn on \"Lower during games\" for apps below to choose what gets lowered (e.g. python for AI training)."
            : "Lowered during games: " + string.Join(", ", _settings.BoostTargets);

    private void OnCheckAction(object sender, RoutedEventArgs e)
    {
        var check = (Check)((FrameworkElement)sender).DataContext;
        try { check.RunAction(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, check.Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        UpdateFixAll();
    }

    private void OnFixAll(object sender, RoutedEventArgs e)
    {
        var failed = new List<string>();
        foreach (var t in _checks.OfType<Tweak>().Where(t => t.NeedsFix))
        {
            try { t.Fix(); }
            catch (Exception ex) { failed.Add($"{t.Title}: {ex.Message}"); }
            t.Refresh();
        }
        UpdateFixAll();
        if (failed.Count > 0)
            MessageBox.Show(this, string.Join("\n", failed), "Some fixes didn't apply", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnBoostToggled(object sender, RoutedEventArgs e)
    {
        if (_app is null) return; // fires during InitializeComponent
        _app.SetGameBoost(BoostToggle.IsChecked == true);
    }

    private void OnGamingToggled(object sender, RoutedEventArgs e)
    {
        if (_app is null) return;
        _app.SetAutoGamingMode(GamingToggle.IsChecked == true);
        UpdateGamingStatus();
    }

    private void UpdateGamingStatus()
    {
        (GamingStatus.Text, GamingStatus.Foreground) =
            !_settings.AutoGamingMode ? ("Off", (System.Windows.Media.Brush)FindResource("Faint"))
            : _app.GamingGame is { } game ? ($"On now: playing {game}, Best performance + Game Boost", (System.Windows.Media.Brush)FindResource("Good"))
            : ("Waiting for a game", (System.Windows.Media.Brush)FindResource("Muted"));
    }

    private void OnGamesKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) OnGamesEdited(sender, e);
    }

    private void OnGamesEdited(object sender, RoutedEventArgs e)
    {
        var games = GamesBox.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (games.SequenceEqual(_settings.Games)) return;
        _settings.Games = games;
        _settings.Save();
        GamesBox.Text = string.Join(", ", games);
    }

    private void OnEndApp(object sender, RoutedEventArgs e)
    {
        var row = (AppRow)((FrameworkElement)sender).DataContext;
        var name = row.Name;
        var answer = MessageBox.Show(this,
            $"End {name}? This closes all {row.ProcessCount} of its processes immediately. Unsaved work in it will be lost.",
            "End app", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        var failed = 0;
        foreach (var p in Process.GetProcessesByName(name))
        {
            using (p)
            {
                try { p.Kill(); }
                catch { failed++; }
            }
        }
        if (failed > 0)
            MessageBox.Show(this, $"{failed} {name} process(es) couldn't be ended (they may need administrator rights).",
                "End app", MessageBoxButton.OK, MessageBoxImage.Information);
        Tick();
    }

    internal void SetBoostTarget(string name, bool target)
    {
        _settings.BoostTargets.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (target) _settings.BoostTargets.Add(name);
        _settings.Save();
        UpdateBoostTargets();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>One row of the "What's using your PC" list.</summary>
    internal sealed class AppRow(OptimizerWindow owner) : INotifyPropertyChanged
    {
        private bool _isBoostTarget;

        public string Name { get; private set; } = "";
        public string Cpu { get; private set; } = "";
        public string Gpu { get; private set; } = "";
        public string Ram { get; private set; } = "";
        public int ProcessCount { get; private set; }
        public bool CanEnd { get; private set; }

        public bool IsBoostTarget
        {
            get => _isBoostTarget;
            set
            {
                if (_isBoostTarget == value) return;
                _isBoostTarget = value;
                owner.SetBoostTarget(Name, value);
            }
        }

        public void Set(AppUsage usage, bool isBoostTarget, bool canEnd)
        {
            Name = usage.Name;
            Cpu = $"{usage.CpuPercent:0}%";
            Gpu = usage.GpuPercent >= 0.5 ? $"{usage.GpuPercent:0}%" : "—";
            Ram = usage.RamMb >= 1024 ? $"{usage.RamMb / 1024:0.0} GB" : $"{usage.RamMb:0} MB";
            ProcessCount = usage.ProcessCount;
            CanEnd = canEnd;
            _isBoostTarget = isBoostTarget;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)); // all properties
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

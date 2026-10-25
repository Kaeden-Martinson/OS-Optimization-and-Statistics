using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using PCSpecOverlay.Core;
using PCSpecOverlay.Monitors;

namespace PCSpecOverlay.Tests;

/// <summary>The hit window sits exactly over the icon, in physical pixels, wherever the overlay is.</summary>
public class LayoutButtonPlacementTests
{
    [Fact]
    public void At_100_percent_dips_are_pixels() =>
        Assert.Equal((1800, 18, 16, 16), OverlayPlacement.HitRect((100, 4, 16, 16), 1700, 14, 1.0));

    [Fact]
    public void At_125_percent_the_icon_is_scaled_from_the_window_corner() =>
        Assert.Equal((2126, 25, 20, 20), OverlayPlacement.HitRect((100.8, 4, 16, 16), 2000, 20, 1.25));

    [Fact]
    public void At_150_percent_half_pixels_round_away_from_the_corner() =>
        // 101 × 1.5 = 151.5 → 152, 117 × 1.5 = 175.5 → 176; 5 × 1.5 = 7.5 → 8, 21 × 1.5 = 31.5 → 32
        Assert.Equal((152, 108, 24, 24), OverlayPlacement.HitRect((101, 5, 16, 16), 0, 100, 1.5));

    [Fact]
    public void A_display_left_of_and_above_the_main_one_has_negative_coordinates() =>
        Assert.Equal((-1499, -1055, 20, 20), OverlayPlacement.HitRect((0.8, 4, 16, 16), -1500, -1060, 1.25));

    [Fact]
    public void An_icon_without_size_has_no_hit_rect()
    {
        Assert.Null(OverlayPlacement.HitRect((10, 4, 0, 16), 0, 0, 1.0));
        Assert.Null(OverlayPlacement.HitRect((10, 4, 16, 16), 0, 0, 0));
    }
}

/// <summary>A click flips the layout in front: the game's own layout while one is in effect, otherwise the normal one.</summary>
public class LayoutToggleTests
{
    [Theory]
    [InlineData(Settings.Compact, null, false, Settings.Expanded)]
    [InlineData(Settings.Expanded, null, false, Settings.Compact)]
    [InlineData(Settings.Compact, Settings.Expanded, true, Settings.Compact)]
    [InlineData(Settings.Expanded, Settings.Expanded, true, Settings.Compact)]
    [InlineData(Settings.Expanded, Settings.Compact, true, Settings.Expanded)]
    public void Flips_the_layout_in_effect(string layout, string? gameLayout, bool forGame, string next) =>
        Assert.Equal((forGame, next), LayoutToggle.Flip(layout, gameLayout));
}

public class LayoutButtonSettingTests
{
    [Fact]
    public void The_button_is_on_by_default() => Assert.True(new Settings().ShowLayoutButton);

    [Fact]
    public void Settings_from_before_the_button_turn_it_on()
    {
        var s = JsonSerializer.Deserialize<Settings>("""{"Layout":"Expanded","ShowFrameTime":false}""")!;
        s.Validate();
        Assert.True(s.ShowLayoutButton);
    }

    [Fact]
    public void Turning_it_off_is_kept()
    {
        var s = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { ShowLayoutButton = false }))!;
        s.Validate();
        Assert.False(s.ShowLayoutButton);
    }
}

public class LayoutButtonSourceTests
{
    private static readonly string Hit = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/LayoutButtonWindow.cs"));

    [Fact]
    public void The_hit_window_never_takes_focus_and_stays_out_of_alt_tab()
    {
        Assert.Contains("WS_EX_NOACTIVATE", Hit);
        Assert.Contains("WS_EX_TOOLWINDOW", Hit);
        Assert.Contains("ShowActivated = false", Hit);
        Assert.Contains("ShowInTaskbar = false", Hit);
        Assert.Contains("Focusable = false", Hit);
        Assert.Contains("MA_NOACTIVATE", Hit);
    }

    [Fact]
    public void The_hit_window_has_a_screen_reader_name_for_each_layout()
    {
        Assert.Contains("AutomationProperties.SetName", Hit);
        Assert.Contains("\"Switch to Panel\"", Hit);
        Assert.Contains("\"Switch to Strip\"", Hit);
    }

    [Fact]
    public void The_overlay_page_has_the_switch()
    {
        var xaml = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/ControlCenterWindow.xaml"));
        Assert.Contains("IsChecked=\"{Binding ShowLayoutButton}\"", xaml);
        Assert.Contains("Text=\"Layout button on the overlay\"", xaml);
        Assert.Contains("A small button at the end of the overlay switches Strip and Panel. A click on it goes to the button, not your game.", xaml);
    }

    [Fact]
    public void The_overlay_draws_the_layout_icon_from_the_icon_set()
    {
        var icons = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/Themes/Icons.xaml"));
        var data = Regex.Match(icons, "<StreamGeometry x:Key=\"Icon.Layout\">([^<]+)</StreamGeometry>").Groups[1].Value;
        Assert.NotEmpty(data);
        var overlay = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/OverlayWindow.xaml"));
        Assert.Contains($"<Setter Property=\"Data\" Value=\"{data}\" />", overlay);
    }
}

/// <summary>The icon's cell has a fixed size: on in the Strip it adds one cell, in the Panel it never widens it.</summary>
public class LayoutButtonWidthTests
{
    private static readonly Telemetry Reading = new()
    {
        Fps = new FpsSample(144, 6.9, 120, 8),
        FpsSource = "DXGI",
        Gpu = new GpuSample(GpuStatus.Ok, 50, 60, 1L << 30, 8L << 30, 1500, 60),
        CpuLoad = 20, CpuTempC = 70, CpuClockGhz = 3.1,
        Ram = (8, 16),
    };

    private static (double Width, OverlayWindow Window) Lay(string layout, bool button)
    {
        var w = new OverlayWindow();
        w.SetGpuName("NVIDIA GeForce RTX 5060 Laptop GPU");
        w.ApplySettings(new Settings { Layout = layout, ShowLayoutButton = button });
        w.ShowFps(Reading);
        w.ShowStats(Reading);
        var root = (UIElement)w.Content;
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
        return (root.DesiredSize.Width, w);
    }

    [Fact]
    public void The_strip_gets_one_fixed_cell_with_the_usual_padding_and_divider() => Sta.Run(() =>
    {
        var (on, w) = Lay(Settings.Compact, true);
        var (off, _) = Lay(Settings.Compact, false);
        var strip = (StackPanel)w.FindName("CompactLayout");
        var cell = (Border)w.FindName("CLayoutBox");
        Assert.Same(cell, strip.Children[^1]);
        Assert.Equal(new Thickness(10, 4, 10, 4), cell.Padding);
        Assert.Equal(new Thickness(1, 0, 0, 0), cell.BorderThickness);
        Assert.Equal(16, ((FrameworkElement)cell.Child).Width);
        Assert.InRange(on - off, 37 - 0.5, 37 + 0.5); // divider 1, padding 10, icon 16, padding 10; layout rounds to device pixels
    });

    [Fact]
    public void The_panel_header_holds_the_icon_without_widening_the_panel() => Sta.Run(() =>
    {
        var (on, w) = Lay(Settings.Expanded, true);
        var (off, _) = Lay(Settings.Expanded, false);
        Assert.Equal(off, on);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)w.FindName("PLayoutIcon")).Visibility);
    });

    [Fact]
    public void Off_removes_the_icon_from_both_layouts() => Sta.Run(() =>
    {
        var (_, w) = Lay(Settings.Compact, false);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)w.FindName("CLayoutBox")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)w.FindName("PLayoutIcon")).Visibility);
    });
}

/// <summary>Clicks come from the hit window's own messages: one click, one toggle, and nothing for a stray up.</summary>
public class LayoutButtonClickTests
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint Down = 0x201, Up = 0x202, DoubleClick = 0x203, Leave = 0x2A3;
    private static readonly IntPtr In = (IntPtr)((3 << 16) | 3), Out = (IntPtr)((3000 << 16) | 3000);

    private static int Clicks(params (uint Msg, IntPtr At)[] messages)
    {
        var count = 0;
        Sta.Run(() =>
        {
            var w = new LayoutButtonWindow(IntPtr.Zero);
            w.Clicked += () => count++;
            foreach (var (msg, at) in messages) SendMessage(w.Handle, msg, msg == Up || msg == Leave ? IntPtr.Zero : (IntPtr)1, at);
            w.Close();
        });
        return count;
    }

    [Fact]
    public void A_click_toggles_once() => Assert.Equal(1, Clicks((Down, In), (Up, In)));

    [Fact]
    public void A_fast_second_click_counts_too() => Assert.Equal(2, Clicks((Down, In), (Up, In), (DoubleClick, In), (Up, In)));

    [Fact]
    public void An_up_without_a_down_does_nothing() => Assert.Equal(0, Clicks((Up, In)));

    [Fact]
    public void Releasing_outside_or_after_leaving_does_nothing()
    {
        Assert.Equal(0, Clicks((Down, In), (Up, Out)));
        Assert.Equal(0, Clicks((Down, In), (Leave, IntPtr.Zero), (Up, In)));
    }

    [Fact]
    public void Screen_reader_invoke_toggles_once() => Sta.Run(() =>
    {
        var count = 0;
        var w = new LayoutButtonWindow(IntPtr.Zero);
        w.Clicked += () => count++;
        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement((UIElement)w.Content);
        ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        w.Close();
        Assert.Equal(1, count);
    });
}

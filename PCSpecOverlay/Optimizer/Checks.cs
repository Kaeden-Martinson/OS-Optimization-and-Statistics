using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using PCSpecOverlay.Core;

namespace PCSpecOverlay.Optimizer;

public enum CheckState { Good, Warn, Info }

/// <summary>One row in the system checks: a status plus an optional action button.</summary>
public abstract class Check : INotifyPropertyChanged
{
    public string Title { get; }
    public string Description { get; }
    public CheckState State { get; private set; }
    public string Status { get; private set; } = "";
    /// <summary>Extra line under the description, e.g. "Recommended: Best performance".</summary>
    public string Detail { get; protected set; } = "";
    public string ActionLabel { get; private set; } = "";
    public bool HasAction => ActionLabel.Length > 0;
    public bool HasDetail => Detail.Length > 0;

    protected Check(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public void Refresh()
    {
        try { (State, Status) = Read(); }
        catch (Exception e) { (State, Status) = (CheckState.Info, $"Couldn't read ({e.Message})"); }
        ActionLabel = CurrentActionLabel();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    /// <returns>A message for the user, or null when the action simply succeeded.</returns>
    public string? RunAction()
    {
        var message = RunActionCore();
        Refresh();
        return message;
    }

    protected abstract (CheckState, string) Read();
    protected abstract string CurrentActionLabel();
    protected abstract string? RunActionCore();

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>A Windows setting the Optimizer can change through the journal, with Undo.</summary>
public class Tweak(IReversibleSetting setting, string optimal, string title, string description,
    Func<string?, string> describe, ChangeJournal journal, string? missingMeans = null) : Check(title, description)
{
    public const string Owner = "optimizer";

    public IReversibleSetting Setting => setting;
    public bool CanUndo => journal.IsOwnedBy(setting.Id, Owner);
    public bool NeedsFix => State == CheckState.Warn && setting.Unsupported is null;

    protected override (CheckState, string) Read()
    {
        if (setting.Unsupported is { } why)
        {
            Detail = why;
            return (CheckState.Info, "Not available");
        }
        var current = setting.Read() ?? missingMeans; // an absent value means the Windows default
        Detail = current == optimal ? "" : $"Recommended: {describe(optimal)}";
        return (current == optimal ? CheckState.Good : CheckState.Warn, describe(current));
    }

    protected override string CurrentActionLabel() => NeedsFix ? "Fix" : CanUndo ? "Undo" : "";

    protected override string? RunActionCore() => NeedsFix ? Fix() : CanUndo ? Undo() : null;

    public virtual string? Fix()
    {
        var r = journal.Apply(setting, optimal, Owner);
        return r.Outcome is ApplyOutcome.Applied or ApplyOutcome.AlreadySet ? null : r.Message;
    }

    public virtual string? Undo()
    {
        var r = journal.Release(setting, Owner);
        return r.Outcome is ReleaseOutcome.Restored ? null : r.Message;
    }
}

/// <summary>Browser graphics preference, which also tracks whether the running browser needs a restart.</summary>
public sealed class GpuPreferenceTweak(BrowserInfo browser, ChangeJournal journal, AppState state)
    : Tweak(new GpuPreferenceSetting(browser.Name, browser.ExePath), GpuPreferenceSetting.HighPerformance,
        $"{browser.Name} graphics chip",
        $"Runs {browser.Name}, and browser games in it, on the NVIDIA GPU instead of the integrated graphics. " +
        $"Benefit: smoother browser games and WebGL. Drawback: uses more battery, and the NVIDIA GPU is shared " +
        $"with anything else running on it (like AI training). Takes effect after {browser.Name} restarts.",
        GpuPreferenceSetting.Describe, journal)
{
    public BrowserInfo Browser => browser;

    public bool NeedsRestart => RestartTracker.NeedsRestart(state, browser.ExePath, Browsers.StartedUtc(browser));

    /// <summary>A previous restart failed and the browser hasn't been seen running since.</summary>
    public bool RestartBroken =>
        state.BrowserRestartFailedUtc.TryGetValue(browser.ExePath, out var failed) &&
        !(Browsers.StartedUtc(browser) is { } started && started > failed);

    public override string? Fix() => Track(base.Fix);
    public override string? Undo() => Track(base.Undo);

    private string? Track(Func<string?> change)
    {
        var before = Setting.Read();
        var message = change();
        RestartTracker.Changed(state, browser.ExePath, before, Setting.Read(), DateTime.UtcNow);
        return message;
    }
}

/// <summary>Laptops cap GPU power hard on battery.</summary>
public sealed class PluggedInCheck() : Check("Power source",
    "Gaming laptops cut GPU power sharply on battery. Plug in for full performance.")
{
    protected override (CheckState, string) Read() =>
        System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus switch
        {
            System.Windows.Forms.PowerLineStatus.Online => (CheckState.Good, "Plugged in"),
            System.Windows.Forms.PowerLineStatus.Offline => (CheckState.Warn, "On battery"),
            _ => (CheckState.Info, "Unknown"),
        };

    protected override string CurrentActionLabel() => "";
    protected override string? RunActionCore() => null;
}

/// <summary>The active power plan, which power mode sits on top of.</summary>
public sealed class PowerPlanCheck() : Check("Power plan",
    "The plan your laptop maker or you selected. Power mode (below) adjusts it; the app never changes the plan itself.")
{
    protected override (CheckState, string) Read() => (CheckState.Info, PowerMode.PlanName() ?? "Unknown");
    protected override string CurrentActionLabel() => "";
    protected override string? RunActionCore() => null;
}

/// <summary>The laptop maker's control app (fan curves, Turbo/Performance mode, power limits).</summary>
public sealed class VendorControlCheck : Check
{
    private readonly string _appId;

    private VendorControlCheck(string name, string appId) : base(name,
        $"{name} controls fan speed and the laptop's performance mode, which set how much power the CPU and GPU may draw. " +
        "For gaming, pick Performance/Turbo and fans on Auto or Max. The app can't change these for you.")
    {
        _appId = appId;
    }

    protected override (CheckState, string) Read() => (CheckState.Info, "Set the mode in the app");
    protected override string CurrentActionLabel() => "Open";

    protected override string? RunActionCore()
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{_appId}") { UseShellExecute = true });
        return null;
    }

    /// <summary>Finds Acer NitroSense / PredatorSense via the installed app packages.</summary>
    public static VendorControlCheck? Find()
    {
        const string packages = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
        using var root = Registry.CurrentUser.OpenSubKey(packages);
        if (root is null) return null;

        foreach (var app in new[] { "NitroSense", "PredatorSense" })
        {
            // Full name: Publisher.Name_Version_Arch__PublisherId -> family: Publisher.Name_PublisherId
            var full = root.GetSubKeyNames().FirstOrDefault(n => n.Contains(app, StringComparison.OrdinalIgnoreCase) && n.Contains("__"));
            if (full is null) continue;
            var family = full[..full.IndexOf('_')] + "_" + full[(full.LastIndexOf("__", StringComparison.Ordinal) + 2)..];
            return new VendorControlCheck(app == "NitroSense" ? "Acer NitroSense" : "Acer PredatorSense", $"{family}!{app}");
        }
        return null;
    }
}

public static class Checks
{
    public static List<Check> Create(ChangeJournal journal, AppState state)
    {
        var list = new List<Check> { new PluggedInCheck(), new PowerPlanCheck() };

        list.Add(new Tweak(SettingCatalog.Power, PowerMode.BestPerformance.ToString(), "Power mode",
            "Best performance lets the CPU boost higher and sooner. Drawback: more heat and less battery life. " +
            "Automatic gaming mode can switch this on only while you play instead.",
            v => PowerMode.Name(Guid.TryParse(v, out var g) ? g : null), journal));

        if (VendorControlCheck.Find() is { } vendor) list.Add(vendor);

        foreach (var browser in Browsers.Installed())
            list.Add(new GpuPreferenceTweak(browser, journal, state));

        list.Add(new Tweak(SettingCatalog.GameMode, "1", "Game Mode",
            "Windows prioritizes the game and holds back Windows Update installs and notifications while you play.",
            v => v is null or "1" ? "On" : "Off", journal, missingMeans: "1"));

        list.Add(new Tweak(SettingCatalog.BackgroundRecording, "0", "Background recording",
            "Xbox Game Bar's \"record what happened\" keeps encoding video all the time, which costs FPS.",
            v => v is null or "0" ? "Off" : "On", journal, missingMeans: "0"));

        foreach (var c in list) c.Refresh();
        return list;
    }
}

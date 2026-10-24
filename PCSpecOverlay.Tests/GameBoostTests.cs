using System.Diagnostics;
using PCSpecOverlay.Core;
using PCSpecOverlay.Optimizer;

namespace PCSpecOverlay.Tests;

/// <summary>Runs against real processes: a copy of ping.exe with a unique name stands in for a game.</summary>
public class GameBoostTests : TempDataTest
{
    private readonly string _name = "pcspec-testgame-" + Guid.NewGuid().ToString("N")[..6];
    private readonly Process _game;
    private readonly AppState _state = new();
    private readonly GameBoost _boost;

    public GameBoostTests()
    {
        var exe = Path.Combine(Dir, _name + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), exe);
        _game = Process.Start(new ProcessStartInfo(exe, "-n 120 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!;
        _boost = new GameBoost(_state);
        _boost.SetEnabled(true);
    }

    private ProcessPriorityClass Priority()
    {
        _game.Refresh();
        return _game.PriorityClass;
    }

    [Fact]
    public void Raises_the_game_one_step_and_restores_it()
    {
        _boost.Tick(_name, []);
        Assert.Equal(ProcessPriorityClass.AboveNormal, Priority());
        Assert.Single(_state.Priorities);

        _boost.Tick(null, []);
        Assert.Equal(ProcessPriorityClass.Normal, Priority());
        Assert.Empty(_state.Priorities);
    }

    [Fact]
    public void Backs_off_when_the_process_changes_its_own_priority()
    {
        _boost.Tick(_name, []);
        _game.PriorityClass = ProcessPriorityClass.Normal; // like a browser resetting its renderers

        _boost.Tick(_name, []);
        Assert.Equal(ProcessPriorityClass.Normal, Priority()); // not fought back
        Assert.Empty(_state.Priorities);

        _boost.Tick(_name, []);
        Assert.Equal(ProcessPriorityClass.Normal, Priority()); // and not re-raised later this game
    }

    [Fact]
    public void Restore_leaves_a_priority_someone_else_changed()
    {
        _boost.Tick(_name, []);
        _game.PriorityClass = ProcessPriorityClass.High;
        _boost.RestoreAll();
        Assert.Equal(ProcessPriorityClass.High, Priority());
    }

    [Fact]
    public void Lowers_chosen_background_apps_and_never_touches_system_processes()
    {
        _boost.Tick("explorer", [_name]);  // a "game" named like a system process is ignored
        Assert.Equal(ProcessPriorityClass.BelowNormal, Priority());
        Assert.DoesNotContain(_state.Priorities, p => p.Name == "explorer");
        _boost.RestoreAll();
        Assert.Equal(ProcessPriorityClass.Normal, Priority());
    }

    [Fact]
    public void Changes_are_recorded_for_crash_recovery()
    {
        _boost.Tick(_name, []);
        var saved = AppState.Load(); // what the next start would see after a crash
        var record = Assert.Single(saved.Priorities);
        Assert.Equal(_game.Id, record.Pid);
        Assert.Equal("Normal", record.Original);

        new GameBoost(saved).RecoverAfterCrash();
        Assert.Equal(ProcessPriorityClass.Normal, Priority());
    }

    public override void Dispose()
    {
        try { _game.Kill(); _game.WaitForExit(2000); } catch { /* already gone */ }
        _game.Dispose();
        base.Dispose();
    }
}

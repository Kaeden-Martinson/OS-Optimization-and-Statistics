using PCSpecOverlay.Core;

namespace PCSpecOverlay.Tests;

public class ChangeJournalTests : TempDataTest
{
    private readonly AppState _state = new();
    private readonly ChangeJournal _journal;

    public ChangeJournalTests() => _journal = new ChangeJournal(_state, TimeProvider.System);

    [Fact]
    public void Apply_records_original_and_release_restores_it()
    {
        var s = new FakeSetting();
        Assert.Equal(ApplyOutcome.Applied, _journal.Apply(s, "fast", "optimizer").Outcome);
        Assert.Equal("fast", s.Value);
        Assert.Equal("original", _journal.Entry(s.Id)!.Original);

        Assert.Equal(ReleaseOutcome.Restored, _journal.Release(s, "optimizer").Outcome);
        Assert.Equal("original", s.Value);
        Assert.Null(_journal.Entry(s.Id));
    }

    [Fact]
    public void Release_keeps_a_newer_user_choice()
    {
        var s = new FakeSetting();
        _journal.Apply(s, "fast", "gaming");
        s.Value = "user-picked"; // changed outside the app

        Assert.Equal(ReleaseOutcome.ChangedElsewhere, _journal.Release(s, "gaming").Outcome);
        Assert.Equal("user-picked", s.Value);
        Assert.Null(_journal.Entry(s.Id));
    }

    [Fact]
    public void Shared_change_is_only_restored_by_the_last_owner()
    {
        var s = new FakeSetting();
        _journal.Apply(s, "fast", "optimizer");
        Assert.Equal(ApplyOutcome.AlreadySet, _journal.Apply(s, "fast", "gaming").Outcome);

        Assert.Equal(ReleaseOutcome.StillNeeded, _journal.Release(s, "gaming").Outcome);
        Assert.Equal("fast", s.Value);
        Assert.Equal(ReleaseOutcome.Restored, _journal.Release(s, "optimizer").Outcome);
        Assert.Equal("original", s.Value);
    }

    [Fact]
    public void Nothing_is_recorded_when_the_value_was_already_set()
    {
        var s = new FakeSetting(initial: "fast");
        Assert.Equal(ApplyOutcome.AlreadySet, _journal.Apply(s, "fast", "gaming").Outcome);
        Assert.Null(_journal.Entry(s.Id));
        Assert.Equal(ReleaseOutcome.NotOwned, _journal.Release(s, "gaming").Outcome);
        Assert.Equal(0, s.Writes);
    }

    [Fact]
    public void Value_that_doesnt_stick_is_reported_and_not_recorded()
    {
        var s = new FakeSetting { IgnoreWrites = true };
        Assert.Equal(ApplyOutcome.Failed, _journal.Apply(s, "fast", "optimizer").Outcome);
        Assert.Null(_journal.Entry(s.Id));
    }

    [Fact]
    public void Write_error_is_reported_and_not_recorded()
    {
        var s = new FakeSetting { ThrowOnWrite = true };
        Assert.Equal(ApplyOutcome.Failed, _journal.Apply(s, "fast", "optimizer").Outcome);
        Assert.Null(_journal.Entry(s.Id));
    }

    [Fact]
    public void Unsupported_setting_is_never_written()
    {
        var s = new FakeSetting { Unsupported = "not on this PC" };
        var r = _journal.Apply(s, "fast", "optimizer");
        Assert.Equal(ApplyOutcome.Unsupported, r.Outcome);
        Assert.Equal("not on this PC", r.Message);
        Assert.Equal(0, s.Writes);
    }

    [Fact]
    public void Failed_restore_keeps_the_entry_for_a_later_attempt()
    {
        var s = new FakeSetting();
        _journal.Apply(s, "fast", "optimizer");
        s.ThrowOnWrite = true;
        Assert.Equal(ReleaseOutcome.Failed, _journal.Release(s, "optimizer").Outcome);
        Assert.True(_journal.IsOwnedBy(s.Id, "optimizer"));

        s.ThrowOnWrite = false;
        Assert.Equal(ReleaseOutcome.Restored, _journal.Release(s, "optimizer").Outcome);
        Assert.Equal("original", s.Value);
    }

    [Fact]
    public void Reapplying_after_an_outside_change_treats_the_new_value_as_original()
    {
        var s = new FakeSetting();
        _journal.Apply(s, "fast", "optimizer");
        s.Value = "user-picked";

        _journal.Apply(s, "fast", "gaming");
        Assert.Equal("user-picked", _journal.Entry(s.Id)!.Original);
        Assert.False(_journal.IsOwnedBy(s.Id, "optimizer")); // stale ownership dropped

        _journal.Release(s, "gaming");
        Assert.Equal("user-picked", s.Value); // never the stale "original"
    }

    [Fact]
    public void Recovery_releases_only_temporary_owners()
    {
        var gamingOnly = new FakeSetting("a");
        var manual = new FakeSetting("b");
        _journal.Apply(gamingOnly, "fast", "gaming");
        _journal.Apply(manual, "fast", "optimizer");

        // Simulate a restart: new journal over the persisted state
        var reloaded = AppState.Load();
        var journal = new ChangeJournal(reloaded, TimeProvider.System);
        journal.RecoverTemporary(["gaming"], id => id == "a" ? gamingOnly : id == "b" ? manual : null);

        Assert.Equal("original", gamingOnly.Value);
        Assert.Equal("fast", manual.Value);
        Assert.True(journal.IsOwnedBy("b", "optimizer"));
    }

    [Fact]
    public void Recovery_drops_entries_for_unknown_settings()
    {
        _state.Changes.Add(new JournalEntry { Id = "from-a-future-version", Original = "x", Applied = "y", Owners = ["gaming"] });
        _journal.RecoverTemporary(["gaming"], _ => null);
        Assert.Empty(_state.Changes);
    }

    [Fact]
    public void State_validation_drops_malformed_entries()
    {
        File.WriteAllText(AppPaths.StateFile, """
            { "Changes": [ { "Id": "", "Owners": ["x"] }, { "Id": "ok", "Owners": [] }, { "Id": "good", "Original": "a", "Applied": "b", "Owners": ["optimizer"] } ],
              "Priorities": [ { "Pid": 0, "Original": "Normal" }, { "Pid": 12, "Original": "NotAPriority" } ] }
            """);
        var state = AppState.Load();
        Assert.Single(state.Changes);
        Assert.Equal("good", state.Changes[0].Id);
        Assert.Empty(state.Priorities);
    }
}

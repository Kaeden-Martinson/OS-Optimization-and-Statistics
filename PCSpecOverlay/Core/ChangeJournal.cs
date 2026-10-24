namespace PCSpecOverlay.Core;

/// <summary>A Windows setting the app may change and later put back.</summary>
public interface IReversibleSetting
{
    /// <summary>Stable id used in the journal, e.g. "power-mode".</summary>
    string Id { get; }

    string Name { get; }

    /// <summary>Null when the setting can be changed on this PC; otherwise the reason it can't.</summary>
    string? Unsupported { get; }

    /// <summary>Current value, serialized; null when the value is absent.</summary>
    string? Read();

    /// <summary>Writes a value; null removes it.</summary>
    void Write(string? value);
}

/// <summary>One change the app made and still owns.</summary>
public sealed class JournalEntry
{
    public string Id { get; set; } = "";
    public string? Original { get; set; }
    public string? Applied { get; set; }
    public List<string> Owners { get; set; } = new();
    public DateTime AppliedUtc { get; set; }
}

public enum ApplyOutcome { Applied, AlreadySet, Unsupported, Failed }

public enum ReleaseOutcome { Restored, StillNeeded, ChangedElsewhere, NotOwned, Failed }

public sealed record ChangeResult<T>(T Outcome, string Message) where T : Enum;

/// <summary>
/// The lifecycle every reversible change goes through:
/// check support → record the original → apply → verify → own → restore (only if still ours) → record failures.
/// <para>
/// Restores are conservative: if the value no longer matches what we wrote, someone else (usually the
/// user) changed it after us, so their newer choice is left alone. Several features can own the same
/// change (e.g. a manual Fix and gaming mode both wanting Best performance); it is only restored when
/// the last owner lets go.
/// </para>
/// </summary>
public sealed class ChangeJournal(AppState state, TimeProvider time)
{
    public IReadOnlyList<JournalEntry> Entries => state.Changes;

    public JournalEntry? Entry(string id) => state.Changes.FirstOrDefault(e => e.Id == id);

    public bool IsOwnedBy(string id, string owner) => Entry(id)?.Owners.Contains(owner) == true;

    public ChangeResult<ApplyOutcome> Apply(IReversibleSetting setting, string? value, string owner)
    {
        if (setting.Unsupported is { } why)
            return new(ApplyOutcome.Unsupported, why);

        string? current;
        try { current = setting.Read(); }
        catch (Exception e) { return Fail(setting, "read", e); }

        var entry = Entry(setting.Id);
        if (entry != null && current != entry.Applied)
        {
            // Changed outside the app since we applied it: the current value is the newest choice,
            // so it becomes the value to return to, and older ownership no longer applies.
            Log.Info("Changes", $"{setting.Name} was changed outside the app; treating '{current}' as the original");
            state.Changes.Remove(entry);
            entry = null;
        }

        if (current == value)
        {
            // Nothing to change. Only join ownership if we changed it earlier; otherwise there is nothing to undo.
            if (entry != null && !entry.Owners.Contains(owner)) entry.Owners.Add(owner);
            state.Save();
            return new(ApplyOutcome.AlreadySet, $"{setting.Name} is already set");
        }

        try
        {
            setting.Write(value);
            if (setting.Read() != value) throw new InvalidOperationException("the new value didn't stick");
        }
        catch (Exception e)
        {
            try { setting.Write(current); } catch { /* best effort rollback */ }
            return Fail(setting, "apply", e);
        }

        entry ??= new JournalEntry { Id = setting.Id, Original = current };
        if (!state.Changes.Contains(entry)) state.Changes.Add(entry);
        entry.Applied = value;
        entry.AppliedUtc = time.GetUtcNow().UtcDateTime;
        if (!entry.Owners.Contains(owner)) entry.Owners.Add(owner);
        state.Save();
        Log.Info("Changes", $"{owner}: set {setting.Name} from '{current}' to '{value}'");
        return new(ApplyOutcome.Applied, $"{setting.Name} changed");
    }

    public ChangeResult<ReleaseOutcome> Release(IReversibleSetting setting, string owner)
    {
        var entry = Entry(setting.Id);
        if (entry is null || !entry.Owners.Remove(owner))
            return new(ReleaseOutcome.NotOwned, $"{setting.Name} wasn't changed by {owner}");

        if (entry.Owners.Count > 0)
        {
            state.Save();
            return new(ReleaseOutcome.StillNeeded, $"{setting.Name} is still needed by {string.Join(", ", entry.Owners)}");
        }

        string? current;
        try { current = setting.Read(); }
        catch (Exception e)
        {
            entry.Owners.Add(owner); // keep the entry so a later attempt can still restore
            state.Save();
            return new(ReleaseOutcome.Failed, $"Couldn't read {setting.Name}: {e.Message}");
        }

        if (current != entry.Applied)
        {
            state.Changes.Remove(entry);
            state.Save();
            Log.Info("Changes", $"{setting.Name} was changed outside the app to '{current}'; leaving it");
            return new(ReleaseOutcome.ChangedElsewhere, $"{setting.Name} was changed since, so your newer choice was kept");
        }

        try
        {
            setting.Write(entry.Original);
            if (setting.Read() != entry.Original) throw new InvalidOperationException("the original value didn't stick");
        }
        catch (Exception e)
        {
            entry.Owners.Add(owner);
            state.Save();
            Log.Error("Changes", $"Couldn't restore {setting.Name}", e);
            return new(ReleaseOutcome.Failed, $"Couldn't restore {setting.Name}: {e.Message}");
        }

        state.Changes.Remove(entry);
        state.Save();
        Log.Info("Changes", $"{owner}: restored {setting.Name} to '{entry.Original}'");
        return new(ReleaseOutcome.Restored, $"{setting.Name} restored");
    }

    /// <summary>
    /// At startup: releases ownership held by temporary features (gaming mode) that can't still be
    /// active, e.g. after a crash. Settings that were changed since are left alone.
    /// Entries for settings this build doesn't know are dropped.
    /// </summary>
    public List<string> RecoverTemporary(IReadOnlyCollection<string> temporaryOwners, Func<string, IReversibleSetting?> resolve)
    {
        var report = new List<string>();
        foreach (var entry in state.Changes.ToList())
        {
            if (resolve(entry.Id) is not { } setting)
            {
                state.Changes.Remove(entry);
                Log.Warn("Changes", $"Dropped a journal entry for unknown setting '{entry.Id}'");
                continue;
            }
            foreach (var owner in entry.Owners.Where(temporaryOwners.Contains).ToList())
                report.Add(Release(setting, owner).Message);
        }
        state.Save();
        return report;
    }

    private static ChangeResult<ApplyOutcome> Fail(IReversibleSetting setting, string step, Exception e)
    {
        Log.Error("Changes", $"Couldn't {step} {setting.Name}", e);
        return new(ApplyOutcome.Failed, $"Couldn't change {setting.Name}: {e.Message}");
    }
}

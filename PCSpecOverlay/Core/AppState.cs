namespace PCSpecOverlay.Core;

/// <summary>A process priority the app changed, identified by pid + start time so a reused pid is never touched.</summary>
public sealed class PriorityChange
{
    public int Pid { get; set; }
    public DateTime StartUtc { get; set; }
    public string Name { get; set; } = "";
    public string Original { get; set; } = "";
    public string Applied { get; set; } = "";
}

/// <summary>
/// A browser graphics preference changed after the browser started. <see cref="LoadedValue"/> is what the
/// running browser picked up, so changing back to it means no restart is needed after all.
/// </summary>
public sealed class PendingRestart
{
    public DateTime ChangedUtc { get; set; }
    public string? LoadedValue { get; set; }
}

/// <summary>
/// Runtime state the app must remember across restarts and crashes, kept apart from user
/// preferences (settings.json) so "restore defaults" never loses track of changes that need undoing.
/// Stored in %AppData%\PCSpecOverlay\state.json.
/// </summary>
public sealed class AppState
{
    public int Version { get; set; } = 1;

    /// <summary>Windows settings the app changed and still owns.</summary>
    public List<JournalEntry> Changes { get; set; } = new();

    /// <summary>Process priorities changed by Game Boost, so they can be put back after a crash.</summary>
    public List<PriorityChange> Priorities { get; set; } = new();

    /// <summary>Browsers whose graphics preference changed while they were running, keyed by exe path.</summary>
    public Dictionary<string, PendingRestart> BrowserRestarts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Browsers whose restart failed, so the app stops offering it until the browser runs again.</summary>
    public Dictionary<string, DateTime> BrowserRestartFailedUtc { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Last automatic update check, and the newest version already announced (announced once each).</summary>
    public DateTime LastUpdateCheckUtc { get; set; }
    public string? AnnouncedVersion { get; set; }

    /// <summary>Starts that didn't reach "running normally"; several in a row turns on recovery mode.</summary>
    public int UnfinishedStarts { get; set; }

    public static AppState Load()
    {
        var state = JsonFile.Load<AppState>(AppPaths.StateFile, "State");
        state.Validate();
        return state;
    }

    public void Save() => JsonFile.Save(AppPaths.StateFile, this, "State");

    /// <summary>Drops malformed entries instead of acting on them.</summary>
    public void Validate()
    {
        Changes ??= new();
        Priorities ??= new();
        BrowserRestarts = new((BrowserRestarts ?? new()).Where(kv => kv.Value is not null), StringComparer.OrdinalIgnoreCase);
        BrowserRestartFailedUtc = new(BrowserRestartFailedUtc ?? new(), StringComparer.OrdinalIgnoreCase);
        Changes.RemoveAll(c => c is null || string.IsNullOrWhiteSpace(c.Id) || c.Owners is null || c.Owners.Count == 0);
        Priorities.RemoveAll(p => p is null || p.Pid <= 0 || !Enum.TryParse<System.Diagnostics.ProcessPriorityClass>(p.Original, out _));
        if (UnfinishedStarts < 0) UnfinishedStarts = 0;
    }
}

namespace Editor.Infrastructure;

/// <summary>Loads and saves bounded editor preferences without delaying document operations.</summary>
public sealed class EditorPreferences
{
    private readonly Lock gate = new();
    private readonly AppStateStore store;
    private Task pending;
    private EditorPresentation current = new();
    private bool wrapChanged;
    private bool numbersChanged;
    private bool fontChanged;
    private long revision;
    private long savedRevision;

    /// <summary>Starts loading preferences, or uses memory only when the path is null.</summary>
    /// <param name="settingsPath">The preference file path.</param>
    public EditorPreferences(string? settingsPath = null)
        : this(new AppStateStore(settingsPath))
    {
    }

    internal EditorPreferences(AppStateStore store)
    {
        this.store = store;
        Ready = Task.Run(LoadAsync);
        pending = Ready;
    }

    /// <summary>Gets the initial read. Invalid storage leaves defaults in effect.</summary>
    public Task Ready { get; }

    /// <summary>Gets an immutable snapshot of the current preferences.</summary>
    public EditorPresentation Current { get { lock (gate) { return current; } } }

    /// <summary>Gets whether the last attempted write failed.</summary>
    public bool StorageFailed { get { lock (gate) { return field; } } private set; }

    /// <summary>Changes supplied preferences. Explicit changes take precedence over the initial read.</summary>
    /// <param name="wordWrap">The wrap preference, or null to preserve it.</param>
    /// <param name="showLineNumbers">The gutter preference, or null to preserve it.</param>
    /// <param name="fontSize">The font size, or null to preserve it.</param>
    public void Update(bool? wordWrap = null, bool? showLineNumbers = null, double? fontSize = null)
    {
        lock (gate)
        {
            wrapChanged |= wordWrap.HasValue;
            numbersChanged |= showLineNumbers.HasValue;
            fontChanged |= fontSize.HasValue;
            EditorPresentation next = new(wordWrap ?? current.WordWrap, showLineNumbers ?? current.ShowLineNumbers,
                fontSize.HasValue ? BoundFont(fontSize.Value) : current.FontSize);
            if (next == current && Ready.IsCompleted && !StorageFailed)
            {
                return;
            }

            current = next;
            revision++;
            if (pending.IsCompleted || ReferenceEquals(pending, Ready))
            {
                pending = Task.Run(SavePendingAsync);
            }
        }
    }

    /// <summary>Gets queued work. Desktop shutdown waits for this task for at most 250 milliseconds.</summary>
    /// <returns>A task that completes after queued writes finish or fail.</returns>
    public Task FlushAsync() { lock (gate) { return pending; } }

    private async Task LoadAsync()
    {
        await store.Ready.ConfigureAwait(false);
        EditorPresentation loaded = store.Presentation;
        lock (gate)
        {
            current = new(wrapChanged ? current.WordWrap : loaded.WordWrap,
                numbersChanged ? current.ShowLineNumbers : loaded.ShowLineNumbers,
                fontChanged ? current.FontSize : loaded.FontSize);
        }
    }

    private static double BoundFont(double value)
    {
        return double.IsFinite(value) ? Math.Clamp(value, EditorPresentation.MinimumFontSize, EditorPresentation.MaximumFontSize)
            : EditorPresentation.DefaultFontSize;
    }

    private async Task SavePendingAsync()
    {
        await Ready.ConfigureAwait(false);
        while (true)
        {
            EditorPresentation snapshot;
            long saving;
            lock (gate)
            {
                if (savedRevision == revision) { return; }
                saving = revision;
                snapshot = current;
            }

            bool failed = !await SaveAsync(snapshot).ConfigureAwait(false);
            lock (gate)
            {
                StorageFailed = failed;
                savedRevision = saving;
                if (savedRevision == revision)
                {
                    pending = Task.CompletedTask;
                    return;
                }
            }
        }
    }

    private async Task<bool> SaveAsync(EditorPresentation snapshot)
    {
        await store.UpdateAsync(presentation: snapshot).ConfigureAwait(false);
        return !store.StorageFailed;
    }
}

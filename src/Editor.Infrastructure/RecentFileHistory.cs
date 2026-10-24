namespace Editor.Infrastructure;

/// <summary>Keeps up to ten local paths and persists them independently of document operations.</summary>
/// <remarks>Windows paths use ordinal case-insensitive comparison. Other systems use ordinal comparison.</remarks>
public sealed class RecentFileHistory
{
    private const int Limit = 10;
    private readonly Lock gate = new();
    private readonly AppStateStore store;
    private Task pending;
    private string[] paths = [];
    private bool cleared;

    /// <summary>Creates an empty history and starts a background settings read when a path is supplied.</summary>
    /// <param name="settingsPath">The settings file, or null for memory-only history.</param>
    public RecentFileHistory(string? settingsPath = null)
        : this(new AppStateStore(settingsPath))
    {
    }

    internal RecentFileHistory(AppStateStore store)
    {
        this.store = store;
        Ready = Task.Run(LoadAsync);
        pending = Ready;
    }

    /// <summary>Gets a snapshot with the most recent path first.</summary>
    public IReadOnlyList<string> Paths
    {
        get
        {
            lock (gate)
            {
                return Array.AsReadOnly(paths);
            }
        }
    }

    /// <summary>Gets whether the most recent settings write failed.</summary>
    public bool StorageFailed
    {
        get
        {
            lock (gate)
            {
                return field;
            }
        }

        private set;
    }

    /// <summary>Gets the initial background read. Storage errors produce an empty history.</summary>
    public Task Ready { get; }

    /// <summary>Records a successfully opened or saved destination without waiting for settings storage.</summary>
    /// <param name="path">The local document path.</param>
    public void Record(string path)
    {
        string? normalized = Normalize(path);
        if (normalized is null)
        {
            return;
        }

        lock (gate)
        {
            paths = Bound([normalized, .. paths]);
            QueueSave();
        }
    }

    /// <summary>Removes all recent paths and queues an empty persisted list.</summary>
    public void Clear()
    {
        lock (gate)
        {
            cleared = true;
            paths = [];
            QueueSave();
        }
    }

    /// <summary>Gets the currently queued settings work, for shutdown and verification.</summary>
    /// <returns>A task that completes after the queued writes finish or fail safely.</returns>
    public Task FlushAsync()
    {
        lock (gate)
        {
            return pending;
        }
    }

    private async Task LoadAsync()
    {
        await store.Ready.ConfigureAwait(false);
        lock (gate)
        {
            if (!cleared) { paths = Bound([.. paths, .. store.Paths]); }
        }
    }

    private static string[] Bound(IEnumerable<string> entries)
    {
        return [.. entries.Select(Normalize).OfType<string>()
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Take(Limit)];
    }

    private static string? Normalize(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        }
        catch (Exception error) when (IsStorageError(error))
        {
            return null;
        }
    }

    private void QueueSave()
    {
        Task previous = pending;
        pending = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);
            string[] snapshot;
            lock (gate)
            {
                snapshot = paths;
            }

            await SaveAsync(snapshot).ConfigureAwait(false);
        });
    }

    private async Task SaveAsync(string[] snapshot)
    {
        await store.UpdateAsync(paths: snapshot).ConfigureAwait(false);
        lock (gate) { StorageFailed = store.StorageFailed; }
    }

    private static bool IsStorageError(Exception error)
    {
        return error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;
    }
}

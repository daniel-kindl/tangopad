using System.Text.Json;

namespace Editor.Infrastructure;

/// <summary>Keeps up to ten local paths and persists them independently of document operations.</summary>
/// <remarks>Windows paths use ordinal case-insensitive comparison. Other systems use ordinal comparison.</remarks>
public sealed class RecentFileHistory
{
    private const int Limit = 10;
    private readonly Lock gate = new();
    private readonly string? settingsPath;
    private Task pending;
    private string[] paths = [];
    private bool cleared;

    /// <summary>Creates an empty history and starts a background settings read when a path is supplied.</summary>
    /// <param name="settingsPath">The settings file, or null for memory-only history.</param>
    public RecentFileHistory(string? settingsPath = null)
    {
        this.settingsPath = settingsPath;
        Ready = settingsPath is null ? Task.CompletedTask : Task.Run(LoadAsync);
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

    /// <summary>Creates history in the current user's platform-specific application-data directory.</summary>
    /// <returns>A persistent history, or memory-only history if no directory is available.</returns>
    public static RecentFileHistory ForCurrentUser()
    {
        try
        {
            string directory = OperatingSystem.IsMacOS()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new(string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, "Tangopad", "recent-files.json"));
        }
        catch (Exception error) when (IsStorageError(error))
        {
            return new();
        }
    }

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
        try
        {
            using FileStream stream = new(settingsPath!, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > 65536)
            {
                return;
            }

            using JsonDocument document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            if (root.GetProperty("version").GetInt32() != 1)
            {
                return;
            }

            string[] loaded = Bound(root.GetProperty("paths").EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .Where(Path.IsPathFullyQualified));
            lock (gate)
            {
                if (!cleared)
                {
                    paths = Bound([.. paths, .. loaded]);
                }
            }
        }
        catch (Exception error) when (IsStorageError(error) || error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            // History failure must not prevent document access.
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
        if (settingsPath is null)
        {
            return;
        }

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
        string? temporary = null;
        try
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(settingsPath!))!;
            _ = Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, $".recent-{Guid.NewGuid():N}.tmp");
            FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (FileStream stream = new(temporary, options))
            {
                using Utf8JsonWriter writer = new(stream);
                writer.WriteStartObject();
                writer.WriteNumber("version", 1);
                writer.WriteStartArray("paths");
                foreach (string path in snapshot)
                {
                    writer.WriteStringValue(path);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                await writer.FlushAsync().ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }

            File.Move(temporary, settingsPath!, overwrite: true);
            lock (gate)
            {
                StorageFailed = false;
            }
        }
        catch (Exception error) when (IsStorageError(error))
        {
            lock (gate)
            {
                StorageFailed = true;
            }
        }
        finally
        {
            if (temporary is not null)
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception error) when (IsStorageError(error))
                {
                    // An inaccessible temporary file can remain after a storage failure.
                }
            }
        }
    }

    private static bool IsStorageError(Exception error)
    {
        return error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;
    }
}

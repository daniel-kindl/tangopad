namespace Editor.Infrastructure;

/// <summary>Owns one binary state file and serializes preference and history writes.</summary>
public sealed class AppStateStore
{
    private readonly Lock writer = new();
    private Task pending;
    private readonly string? path;
    private readonly string? legacyDirectory;
    private BinaryAppState state = new(new(), []);
    private bool protectedFile;

    /// <summary>Starts loading binary state and imports legacy JSON only when state is absent.</summary>
    /// <param name="path">The binary file path, or null for memory-only state.</param>
    /// <param name="legacyDirectory">The same mode's legacy directory, or null to disable import.</param>
    public AppStateStore(string? path = null, string? legacyDirectory = null)
    {
        this.path = path is null ? null : Path.GetFullPath(path);
        this.legacyDirectory = legacyDirectory;
        Ready = Task.Run(LoadAsync);
        pending = Ready;
        Preferences = new(this);
        History = new(this);
    }

    /// <summary>Gets the preference consumer of this state file.</summary>
    public EditorPreferences Preferences { get; }

    /// <summary>Gets the history consumer of this state file.</summary>
    public RecentFileHistory History { get; }

    /// <summary>Gets the initial load and migration task.</summary>
    public Task Ready { get; }

    /// <summary>Gets the last storage error, or null when storage succeeds.</summary>
    public string? StorageError { get; private set; }

    internal bool StorageFailed => StorageError is not null;
    internal EditorPresentation Presentation => state.Presentation;
    internal string[] Paths => state.Paths;

    internal Task UpdateAsync(EditorPresentation? presentation = null, string[]? paths = null)
    {
        lock (writer)
        {
            Task previous = pending;
            pending = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                state = new(presentation ?? state.Presentation, paths ?? state.Paths);
                if (!protectedFile) { await SaveAsync().ConfigureAwait(false); }
            });
            return pending;
        }
    }

    private async Task LoadAsync()
    {
        if (path is null) { return; }
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            byte[] bytes = new byte[BinaryAppState.MaximumSize + 1];
            int length = await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false).ConfigureAwait(false);
            state = BinaryAppState.Decode(bytes[..length]);
            CheckWriteAccess();
            return;
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception error) when (IsStorageError(error))
        {
            protectedFile = true;
            StorageError = $"Cannot load state. The existing file will remain unchanged. {error.Message}";
            return;
        }

        if (legacyDirectory is not null)
        {
            try
            {
                state = await LegacyAppState.LoadAsync(legacyDirectory).ConfigureAwait(false);
                await SaveAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (IsStorageError(error) || error is System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
            {
                protectedFile = true;
                StorageError = $"Cannot migrate legacy state. The legacy files will remain unchanged. {error.Message}";
            }
        }
    }

    private void CheckWriteAccess()
    {
        try
        {
            string directory = Path.GetDirectoryName(path!)!;
            using FileStream probe = new(Path.Combine(directory, $".state-{Guid.NewGuid():N}.tmp"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (Exception error) when (IsStorageError(error))
        {
            StorageError = $"Cannot write state. Check access to {path}. {error.Message}";
        }
    }

    private async Task SaveAsync()
    {
        if (path is null) { return; }
        string? temporary = null;
        try
        {
            byte[] bytes = state.Encode();
            string directory = Path.GetDirectoryName(path)!;
            _ = Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, $".state-{Guid.NewGuid():N}.tmp");
            FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) { options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }
            using (FileStream stream = new(temporary, options))
            {
                await stream.WriteAsync(bytes).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                FlushToDisk(stream);
            }

            // Verify the complete temporary file before committing a migration or update.
            _ = BinaryAppState.Decode(await File.ReadAllBytesAsync(temporary).ConfigureAwait(false));
            File.Move(temporary, path, overwrite: true);
            StorageError = null;
        }
        catch (Exception error) when (IsStorageError(error))
        {
            StorageError = $"Cannot save state. Check access to {path}. {error.Message}";
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception error) when (IsStorageError(error)) { /* An inaccessible temporary file can remain. */ }
            }
        }
    }

    private static void FlushToDisk(FileStream stream)
    {
        stream.Flush(flushToDisk: true);
    }

    internal static bool IsStorageError(Exception error)
    {
        return error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;
    }
}

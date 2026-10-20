using System.Text.Json;

namespace Editor.Infrastructure;

/// <summary>Loads and saves bounded editor preferences without delaying document operations.</summary>
public sealed class EditorPreferences
{
    private readonly Lock gate = new();
    private readonly string? settingsPath;
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
    {
        this.settingsPath = settingsPath;
        Ready = settingsPath is null ? Task.CompletedTask : Task.Run(LoadAsync);
        pending = Ready;
    }

    /// <summary>Gets the initial read. Invalid storage leaves defaults in effect.</summary>
    public Task Ready { get; }

    /// <summary>Gets an immutable snapshot of the current preferences.</summary>
    public EditorPresentation Current { get { lock (gate) { return current; } } }

    /// <summary>Gets whether the last attempted write failed.</summary>
    public bool StorageFailed { get { lock (gate) { return field; } } private set; }

    /// <summary>Creates storage in the same per-user directory as recent files.</summary>
    /// <returns>Persistent preferences, or memory-only preferences if the location is unavailable.</returns>
    public static EditorPreferences ForCurrentUser()
    {
        try
        {
            return new(UserDataLocation.FilePath("editor-preferences.json"));
        }
        catch (Exception error) when (IsStorageError(error))
        {
            return new();
        }
    }

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
            if (settingsPath is not null && (pending.IsCompleted || ReferenceEquals(pending, Ready)))
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
        try
        {
            using FileStream stream = new(settingsPath!, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            byte[] bytes = new byte[4097];
            int length = await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false).ConfigureAwait(false);
            if (length > 4096) { return; }
            using JsonDocument document = JsonDocument.Parse(bytes.AsMemory(0, length));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out JsonElement version)
                || !version.TryGetInt32(out int number) || number != 1) { return; }
            bool wrap = ReadBool(root, "wordWrap", false);
            bool numbers = ReadBool(root, "showLineNumbers", true);
            double font = root.TryGetProperty("fontSize", out JsonElement size) && size.ValueKind == JsonValueKind.Number
                && size.TryGetDouble(out double value) ? BoundFont(value) : EditorPresentation.DefaultFontSize;
            lock (gate)
            {
                current = new(wrapChanged ? current.WordWrap : wrap, numbersChanged ? current.ShowLineNumbers : numbers,
                    fontChanged ? current.FontSize : font);
            }
        }
        catch (Exception error) when (IsStorageError(error) || error is JsonException or InvalidOperationException)
        {
            // Invalid preferences leave defaults and explicit changes in effect.
        }
    }

    private static bool ReadBool(JsonElement root, string key, bool fallback)
    {
        return root.TryGetProperty(key, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : fallback;
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
        string? temporary = null;
        try
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(settingsPath!))!;
            _ = Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, $".preferences-{Guid.NewGuid():N}.tmp");
            FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) { options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }
            using (FileStream stream = new(temporary, options))
            {
                await JsonSerializer.SerializeAsync(stream, new
                {
                    version = 1,
                    wordWrap = snapshot.WordWrap,
                    showLineNumbers = snapshot.ShowLineNumbers,
                    fontSize = snapshot.FontSize
                }).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }

            File.Move(temporary, settingsPath!, overwrite: true);
            return true;
        }
        catch (Exception error) when (IsStorageError(error)) { return false; }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception error) when (IsStorageError(error)) { /* Cleanup is best effort. */ }
            }
        }
    }

    private static bool IsStorageError(Exception error)
    {
        return error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;
    }
}

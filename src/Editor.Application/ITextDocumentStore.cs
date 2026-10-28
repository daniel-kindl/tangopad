namespace Editor.Application;

/// <summary>Transfers exact plain text between the application and a persistence adapter.</summary>
/// <remarks>
/// The adapter owns filesystem operations, encoding, and destination interpretation.
/// Failures propagate as exceptions. Cancellation propagates as OperationCanceledException.
/// </remarks>
public interface ITextDocumentStore
{
    /// <summary>Loads exact text without application-level normalization.</summary>
    /// <param name="destination">A nonblank destination interpreted by the adapter.</param>
    /// <param name="cancellationToken">The token that cancels the load.</param>
    /// <returns>The exact loaded text and the revision of its source bytes.</returns>
    Task<StoredDocument> LoadAsync(string destination, CancellationToken cancellationToken);

    /// <summary>Saves a captured text snapshot.</summary>
    /// <param name="destination">A nonblank destination interpreted by the adapter.</param>
    /// <param name="text">The exact snapshot to persist.</param>
    /// <param name="expectedRevision">The last loaded or saved revision, or null before the first persistence.</param>
    /// <param name="overwrite">Whether the caller explicitly permits replacing the current destination.</param>
    /// <param name="cancellationToken">The token that cancels the save.</param>
    /// <returns>The persisted revision and whether it reached the disk, or null for a conflict with no write.</returns>
    /// <remarks>A failed or cancelled task must never report successful persistence.</remarks>
    Task<SaveResult?> SaveAsync(string destination, string text, string? expectedRevision, bool overwrite, CancellationToken cancellationToken);
}

/// <summary>Contains exact text and an adapter-owned revision of the source bytes.</summary>
/// <param name="text">The decoded text.</param>
/// <param name="revision">The opaque revision, including destination identity.</param>
public sealed class StoredDocument(string text, string revision)
{
    /// <summary>Gets the decoded text.</summary>
    public string Text { get; } = text;

    /// <summary>Gets the opaque revision, including destination identity.</summary>
    public string Revision { get; } = revision;
}

/// <summary>Contains the revision of a written snapshot and whether the adapter could flush it to the disk.</summary>
/// <param name="Revision">The opaque revision of the written bytes, including destination identity.</param>
/// <param name="Flushed">False when the snapshot is in place but the adapter could not flush it to the disk.</param>
public sealed record SaveResult(string Revision, bool Flushed);

using Editor.Core;

namespace Editor.Application;

/// <summary>Orchestrates plain-text load and save for one open document.</summary>
/// <remarks>
/// Callers must serialize access to the session and its document, including asynchronous continuations.
/// Edits are permitted while a save awaits persistence. Overlapping saves are rejected.
/// This class retains the caller's synchronization context. It does not provide thread synchronization.
/// </remarks>
public sealed class DocumentSession
{
    /// <summary>The message when the file was written but the store could not flush it to the disk.</summary>
    public const string NotFlushedMessage = "";

    private readonly ITextDocumentStore store;
    private bool saving;

    private DocumentSession(ITextDocumentStore store, TextDocument document, string? destination)
    {
        this.store = store;
        Document = document;
        Destination = destination;
    }

    /// <summary>Gets the document whose identity survives edits and destination changes.</summary>
    public TextDocument Document { get; }

    /// <summary>Gets the last successfully loaded or saved destination, or null for an untitled document.</summary>
    /// <remarks>The destination is opaque. The session does not normalize paths or test file existence.</remarks>
    public string? Destination { get; private set; }

    /// <summary>Gets the last successfully loaded or saved opaque disk revision.</summary>
    public string? Revision { get; private set; }

    /// <summary>Gets the format identified from the last successful destination, or plain text when untitled.</summary>
    public DocumentFormat Format => DocumentFormats.Identify(Destination);

    /// <summary>Creates an empty, clean document without a destination.</summary>
    /// <param name="store">The persistence adapter for this session.</param>
    /// <returns>The untitled session.</returns>
    public static DocumentSession CreateUntitled(ITextDocumentStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new(store, TextDocument.CreateUntitled(), null);
    }

    /// <summary>Loads supplied text as the clean baseline of a new document.</summary>
    /// <param name="store">The persistence adapter for this session.</param>
    /// <param name="destination">The nonblank destination to load.</param>
    /// <param name="cancellationToken">The token that cancels the load.</param>
    /// <returns>A new clean session after a successful load.</returns>
    public static async Task<DocumentSession> LoadAsync(ITextDocumentStore store, string destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        cancellationToken.ThrowIfCancellationRequested();
        StoredDocument loaded = await store.LoadAsync(destination, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        return new(store, TextDocument.FromText(loaded.Text), destination) { Revision = loaded.Revision };
    }

    /// <summary>Saves a captured snapshot to the current destination, even when the document is clean.</summary>
    /// <param name="cancellationToken">The token that cancels the save.</param>
    /// <returns>True after persistence and acknowledgement, or false for a conflict without a write.</returns>
    /// <exception cref="InvalidOperationException">The document has no destination, or a save is already pending.</exception>
    public Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        return SaveAsAsync(Destination ?? throw new InvalidOperationException(
            "The document has no destination. Use SaveAsAsync first."), false, cancellationToken);
    }

    /// <summary>Saves a snapshot and changes the destination only after success.</summary>
    /// <param name="destination">The nonblank destination for the snapshot.</param>
    /// <param name="cancellationToken">The token that cancels the save.</param>
    /// <param name="overwrite">Whether the user explicitly permits replacing the current disk state.</param>
    /// <returns>True after persistence and acknowledgement, or false for a conflict without a write.</returns>
    /// <exception cref="InvalidOperationException">A save is already pending.</exception>
    /// <remarks>
    /// Failure or observed cancellation preserves the baseline and destination.
    /// If the adapter ignores cancellation, cancellation observed after persistence still prevents acknowledgement.
    /// The adapter might have written data in that case. A retry remains possible.
    /// </remarks>
    public async Task<bool> SaveAsAsync(string destination, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        cancellationToken.ThrowIfCancellationRequested();
        if (saving)
        {
            throw new InvalidOperationException("A save is already pending for this document.");
        }

        saving = true;
        try
        {
            string snapshot = Document.Text;
            SaveResult? persisted = await store.SaveAsync(destination, snapshot, Revision, overwrite, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (persisted is null)
            {
                return false;
            }

            Revision = persisted.Revision;
            Document.AcceptChanges(snapshot);
            Destination = destination;
            return true;
        }
        finally
        {
            saving = false;
        }
    }
}

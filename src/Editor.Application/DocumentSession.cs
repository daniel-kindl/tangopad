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
        string text = await store.LoadAsync(destination, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        return new(store, TextDocument.FromText(text), destination);
    }

    /// <summary>Saves a captured snapshot to the current destination, even when the document is clean.</summary>
    /// <param name="cancellationToken">The token that cancels the save.</param>
    /// <returns>A task that completes after persistence and acknowledgement.</returns>
    /// <exception cref="InvalidOperationException">The document has no destination, or a save is already pending.</exception>
    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        return SaveAsAsync(Destination ?? throw new InvalidOperationException(
            "The document has no destination. Use SaveAsAsync first."), cancellationToken);
    }

    /// <summary>Saves a snapshot and changes the destination only after success.</summary>
    /// <param name="destination">The nonblank destination for the snapshot.</param>
    /// <param name="cancellationToken">The token that cancels the save.</param>
    /// <returns>A task that completes after persistence, acknowledgement, and the destination update.</returns>
    /// <exception cref="InvalidOperationException">A save is already pending.</exception>
    /// <remarks>
    /// Failure or observed cancellation preserves the baseline and destination.
    /// If the adapter ignores cancellation, cancellation observed after persistence still prevents acknowledgement.
    /// The adapter might have written data in that case. A retry remains possible.
    /// </remarks>
    public async Task SaveAsAsync(string destination, CancellationToken cancellationToken = default)
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
            await store.SaveAsync(destination, snapshot, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            Document.AcceptChanges(snapshot);
            Destination = destination;
        }
        finally
        {
            saving = false;
        }
    }
}

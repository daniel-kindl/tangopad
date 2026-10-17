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
    /// <returns>The exact loaded text, including empty text.</returns>
    Task<string> LoadAsync(string destination, CancellationToken cancellationToken);

    /// <summary>Saves a captured text snapshot.</summary>
    /// <param name="destination">A nonblank destination interpreted by the adapter.</param>
    /// <param name="text">The exact snapshot to persist.</param>
    /// <param name="cancellationToken">The token that cancels the save.</param>
    /// <returns>A task that succeeds only after the adapter completes persistence.</returns>
    /// <remarks>A failed or cancelled task must never report successful persistence.</remarks>
    Task SaveAsync(string destination, string text, CancellationToken cancellationToken);
}

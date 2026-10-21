namespace Editor.Application;

/// <summary>Supplies optional file notifications and verifies opaque disk revisions.</summary>
public interface IDocumentFileMonitor
{
    /// <summary>Monitors one destination until the returned subscription is disposed.</summary>
    /// <param name="destination">The local destination.</param>
    /// <param name="changed">A thread-safe callback for events, including monitoring errors.</param>
    /// <returns>The subscription that owns monitoring resources.</returns>
    /// <remarks>Events are hints. Consumers must verify the revision. Monitoring can fail or miss events.</remarks>
    IDisposable Watch(string destination, Action changed);

    /// <summary>Reads the revision used by persistence without decoding or changing text.</summary>
    /// <param name="destination">The local destination.</param>
    /// <param name="cancellationToken">The token that cancels verification.</param>
    /// <returns>The current revision, or null when the destination is missing.</returns>
    Task<string?> ReadRevisionAsync(string destination, CancellationToken cancellationToken);
}

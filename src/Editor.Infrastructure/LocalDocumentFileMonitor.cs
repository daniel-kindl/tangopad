using Editor.Application;

namespace Editor.Infrastructure;

/// <summary>Monitors one local directory entry with filesystem event hints.</summary>
public sealed class LocalDocumentFileMonitor : IDocumentFileMonitor
{
    /// <inheritdoc />
    public IDisposable Watch(string destination, Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        string path = Path.GetFullPath(destination);
        FileSystemWatcher watcher = new(Path.GetDirectoryName(path)!, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
        };
        watcher.Changed += (_, _) => changed();
        watcher.Created += (_, _) => changed();
        watcher.Deleted += (_, _) => changed();
        watcher.Renamed += (_, _) => changed();
        watcher.Error += (_, _) => changed();
        try
        {
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch
        {
            watcher.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public Task<string?> ReadRevisionAsync(string destination, CancellationToken cancellationToken)
    {
        return FileTextDocumentStore.ReadRevisionAsync(Path.GetFullPath(destination), null, cancellationToken);
    }
}

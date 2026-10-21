using Editor.Application;
using Xunit;

namespace Editor.Infrastructure.Tests;

/// <summary>Checks that notifications and save protection use the same raw-byte revisions.</summary>
public sealed class DocumentFileMonitorTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("tangopad-monitor-").FullName;
    private readonly LocalDocumentFileMonitor monitor = new();

    /// <summary>Monitoring preserves BOM distinctions and detects bytes with restored metadata.</summary>
    [Fact]
    public async Task RevisionsMatchLoadedAndSavedBytes()
    {
        string path = Path.Combine(directory, "document.md");
        await File.WriteAllBytesAsync(path, [0xEF, 0xBB, 0xBF, 0x61], TestContext.Current.CancellationToken);
        DocumentSession session = await DocumentSession.LoadAsync(new FileTextDocumentStore(), path, TestContext.Current.CancellationToken);
        Assert.Equal(session.Revision, await monitor.ReadRevisionAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal("a", session.Document.Text);
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        await File.WriteAllBytesAsync(path, [0xEF, 0xBB, 0xBF, 0xFF], TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(path, timestamp);
        Assert.NotEqual(session.Revision, await monitor.ReadRevisionAsync(path, TestContext.Current.CancellationToken));
        Assert.False(await session.SaveAsync(TestContext.Current.CancellationToken));
        Assert.True(await session.SaveAsAsync(path, true, TestContext.Current.CancellationToken));
        Assert.Equal(session.Revision, await monitor.ReadRevisionAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal("a"u8.ToArray(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>A native watcher reports replacement and does not follow the renamed file.</summary>
    [Fact]
    public async Task WatcherChecksTheDestinationAfterReplacement()
    {
        string path = Path.Combine(directory, "current.txt");
        string replacement = Path.Combine(directory, "replacement.txt");
        await File.WriteAllTextAsync(path, "original", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(replacement, "replacement", TestContext.Current.CancellationToken);
        string? before = await monitor.ReadRevisionAsync(path, TestContext.Current.CancellationToken);
        TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = monitor.Watch(path, () => signal.TrySetResult());
        File.Move(replacement, path, true);
        await signal.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.NotEqual(before, await monitor.ReadRevisionAsync(path, TestContext.Current.CancellationToken));
        File.Move(path, Path.Combine(directory, "renamed.txt"));
        Assert.Null(await monitor.ReadRevisionAsync(path, TestContext.Current.CancellationToken));
        Directory.Delete(directory, true);
        Assert.Null(await monitor.ReadRevisionAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>Revision verification observes cancellation without reading file contents.</summary>
    [Fact]
    public async Task VerificationCanBeCancelled()
    {
        string path = Path.Combine(directory, "current.txt");
        await File.WriteAllTextAsync(path, "original", TestContext.Current.CancellationToken);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => monitor.ReadRevisionAsync(path, cancelled.Token));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(directory)) { Directory.Delete(directory, true); }
    }
}

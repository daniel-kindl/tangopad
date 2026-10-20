using System.Text;
using Editor.Application;
using Xunit;

namespace Editor.Infrastructure.Tests;

/// <summary>Checks optimistic save concurrency against real file bytes.</summary>
public sealed class ExternalChangeTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("tangopad-conflict-").FullName;
    private readonly FileTextDocumentStore store = new();

    /// <summary>External edits must survive an attempted save, including equal-length edits with a restored timestamp.</summary>
    /// <param name="restoreTimestamp">Whether to restore the original write timestamp.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalEditPreservesDiskAndSession(bool restoreTimestamp)
    {
        string path = Path.Combine(directory, "document.txt");
        await File.WriteAllTextAsync(path, "first", TestContext.Current.CancellationToken);
        DocumentSession session = await DocumentSession.LoadAsync(store, path, TestContext.Current.CancellationToken);
        Guid identity = session.Document.Id;
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        session.Document.ReplaceText("local 😀\r\nλ\nlast\r");
        await File.WriteAllTextAsync(path, "other", TestContext.Current.CancellationToken);
        if (restoreTimestamp)
        {
            File.SetLastWriteTimeUtc(path, timestamp);
        }

        Assert.False(await session.SaveAsync(TestContext.Current.CancellationToken));

        Assert.Equal(Encoding.UTF8.GetBytes("other"), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal("local 😀\r\nλ\nlast\r", session.Document.Text);
        Assert.True(session.Document.IsDirty);
        Assert.Equal(identity, session.Document.Id);
        Assert.Equal(path, session.Destination);
        Assert.Equal([path], Directory.GetFileSystemEntries(directory));
    }

    /// <summary>Missing and replaced destinations require an explicit overwrite.</summary>
    /// <param name="replacement">Whether a replacement appears after rename.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovalAndReplacementRequireOverwrite(bool replacement)
    {
        string path = Path.Combine(directory, "current.txt");
        string renamed = Path.Combine(directory, "moved.txt");
        await File.WriteAllTextAsync(path, "original", TestContext.Current.CancellationToken);
        DocumentSession session = await DocumentSession.LoadAsync(store, path, TestContext.Current.CancellationToken);
        session.Document.ReplaceText("local");
        File.Move(path, renamed);
        if (replacement)
        {
            await File.WriteAllTextAsync(path, "replacement", TestContext.Current.CancellationToken);
        }

        Assert.False(await session.SaveAsync(TestContext.Current.CancellationToken));
        Assert.Equal(replacement, File.Exists(path));
        Assert.True(session.Document.IsDirty);
        Assert.True(await session.SaveAsAsync(path, true, TestContext.Current.CancellationToken));
        Assert.Equal("local"u8.ToArray(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal("original"u8.ToArray(), await File.ReadAllBytesAsync(renamed, TestContext.Current.CancellationToken));
        Assert.False(session.Document.IsDirty);
        session.Document.ReplaceText("next");
        Assert.True(await session.SaveAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Independent instances retain separate revisions after repeated saves and reopen.</summary>
    [Fact]
    public async Task CompetingInstancesRejectStaleSaves()
    {
        string path = Path.Combine(directory, "shared.md");
        await File.WriteAllBytesAsync(path, [0xef, 0xbb, 0xbf, 0x61], TestContext.Current.CancellationToken);
        DocumentSession first = await DocumentSession.LoadAsync(store, path, TestContext.Current.CancellationToken);
        DocumentSession second = await DocumentSession.LoadAsync(new FileTextDocumentStore(), path, TestContext.Current.CancellationToken);
        first.Document.ReplaceText("first 😀\r\nλ\nlast\r");
        Assert.True(await first.SaveAsync(TestContext.Current.CancellationToken));
        Assert.True(await first.SaveAsync(TestContext.Current.CancellationToken));
        DocumentSession reopened = await DocumentSession.LoadAsync(store, path, TestContext.Current.CancellationToken);
        Assert.True(await reopened.SaveAsync(TestContext.Current.CancellationToken));
        second.Document.ReplaceText("second");
        Assert.False(await second.SaveAsync(TestContext.Current.CancellationToken));
        Assert.Equal(Encoding.UTF8.GetBytes("first 😀\r\nλ\nlast\r"), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.True(await second.SaveAsAsync(path, true, TestContext.Current.CancellationToken));
        Assert.Equal("second"u8.ToArray(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.False(await first.SaveAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Save As to an existing path requires overwrite and retains the original file.</summary>
    [Fact]
    public async Task SaveAsCannotUseOriginalRevisionForAnotherDestination()
    {
        string original = Path.Combine(directory, "original.txt");
        string destination = Path.Combine(directory, "existing.md");
        await File.WriteAllTextAsync(original, "original", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(destination, "external", TestContext.Current.CancellationToken);
        DocumentSession session = await DocumentSession.LoadAsync(store, original, TestContext.Current.CancellationToken);
        Guid identity = session.Document.Id;
        session.Document.ReplaceText("local");
        Assert.False(await session.SaveAsAsync(destination, false, TestContext.Current.CancellationToken));
        Assert.Equal(original, session.Destination);
        Assert.Equal(identity, session.Document.Id);
        Assert.True(session.Document.IsDirty);
        Assert.Equal("external"u8.ToArray(), await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
        Assert.True(await session.SaveAsAsync(destination, true, TestContext.Current.CancellationToken));
        Assert.Equal(destination, session.Destination);
        Assert.Equal(identity, session.Document.Id);
        Assert.Equal("original"u8.ToArray(), await File.ReadAllBytesAsync(original, TestContext.Current.CancellationToken));
        Assert.True(await session.SaveAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Failed overwrite preserves bytes, dirty state, and the previous disk revision.</summary>
    [Fact]
    public async Task FailedOverwriteKeepsRevisionAndCleansTemporaryFile()
    {
        string path = Path.Combine(directory, "current.txt");
        await File.WriteAllTextAsync(path, "original", TestContext.Current.CancellationToken);
        DocumentSession session = await DocumentSession.LoadAsync(store, path, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(path, "external", TestContext.Current.CancellationToken);
        session.Document.ReplaceText("invalid \ud800");
        _ = await Assert.ThrowsAsync<EncoderFallbackException>(() => session.SaveAsAsync(path, true, TestContext.Current.CancellationToken));
        Assert.Equal("external"u8.ToArray(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.True(session.Document.IsDirty);
        Assert.Equal([path], Directory.GetFileSystemEntries(directory));
        session.Document.ReplaceText("valid");
        Assert.False(await session.SaveAsync(TestContext.Current.CancellationToken));
        Assert.True(await session.SaveAsAsync(path, true, TestContext.Current.CancellationToken));
    }

    /// <summary>Removes the isolated test directory.</summary>
    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
    }
}

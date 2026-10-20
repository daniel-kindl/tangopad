using System.Text;
using Editor.Application;
using Editor.Core;
using Xunit;

namespace Editor.Infrastructure.Tests;

/// <summary>Checks local persistence using files in an isolated temporary directory.</summary>
public sealed class FileTextDocumentStoreTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("tangopad-store-").FullName;
    private readonly FileTextDocumentStore store = new();

    /// <summary>Checks exact text and predictable UTF-8 output.</summary>
    /// <param name="text">The text to persist.</param>
    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("Příliš 🐈\r\n漢字\nالعربية\r\t\0e\u0301")]
    public async Task RoundTripPreservesTextAndWritesUtf8WithoutBom(string text)
    {
        string path = Path.Combine(directory, "document.txt");

        _ = await store.SaveAsync(path, text, null, true, TestContext.Current.CancellationToken);

        Assert.Equal(Encoding.UTF8.GetBytes(text), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(text, (await store.LoadAsync(path, TestContext.Current.CancellationToken)).Text);
        Assert.Equal([path], Directory.GetFileSystemEntries(directory));
    }

    /// <summary>Checks that format transitions preserve persisted bytes and document state.</summary>
    /// <param name="extension">The extension of the new destination.</param>
    /// <param name="format">The expected format after save-as.</param>
    [Theory]
    [InlineData(".md", DocumentFormat.Markdown)]
    [InlineData(".MARKDOWN", DocumentFormat.Markdown)]
    [InlineData(".unknown", DocumentFormat.PlainText)]
    [InlineData("", DocumentFormat.PlainText)]
    public async Task FormatTransitionsPreserveUtf8Bytes(string extension, DocumentFormat format)
    {
        const string text = "# Příliš 🐈\r\n漢字\n\0e\u0301";
        string original = Path.Combine(directory, "original.txt");
        string destination = Path.Combine(directory, "renamed" + extension);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await File.WriteAllBytesAsync(original, bytes, TestContext.Current.CancellationToken);
        DocumentSession session = await DocumentSession.LoadAsync(store, original, TestContext.Current.CancellationToken);
        Guid identity = session.Document.Id;

        _ = await session.SaveAsAsync(destination, true, TestContext.Current.CancellationToken);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(original, TestContext.Current.CancellationToken));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
        Assert.Equal(format, session.Format);
        Assert.Equal(identity, session.Document.Id);
        Assert.Equal(text, session.Document.Text);
        Assert.False(session.Document.IsDirty);
        DocumentSession reopened = await DocumentSession.LoadAsync(store, destination, TestContext.Current.CancellationToken);
        Assert.Equal(format, reopened.Format);
        Assert.Equal(text, reopened.Document.Text);
    }

    /// <summary>Checks that one initial UTF-8 BOM is metadata and all other characters remain text.</summary>
    [Fact]
    public async Task LoadRemovesOnlyOneInitialBom()
    {
        string path = Path.Combine(directory, "bom.txt");
        await File.WriteAllBytesAsync(path, [0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF, 0x61], TestContext.Current.CancellationToken);

        Assert.Equal("\uFEFFa", (await store.LoadAsync(path, TestContext.Current.CancellationToken)).Text);

        _ = await store.SaveAsync(path, "a", null, true, TestContext.Current.CancellationToken);
        Assert.Equal("a"u8.ToArray(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>Checks rejection of malformed UTF-8 and unsupported BOM encodings.</summary>
    /// <param name="hex">The input bytes as hexadecimal text.</param>
    [Theory]
    [InlineData("C328")]
    [InlineData("E282")]
    [InlineData("EDA080")]
    [InlineData("C0AF")]
    [InlineData("FFFE6100")]
    [InlineData("FEFF0061")]
    [InlineData("FFFE000061000000")]
    [InlineData("0000FEFF00000061")]
    public async Task InvalidUtf8FailsWithoutReplacementCharacters(string hex)
    {
        string path = Path.Combine(directory, "invalid.txt");
        await File.WriteAllBytesAsync(path, Convert.FromHexString(hex), TestContext.Current.CancellationToken);

        _ = await Assert.ThrowsAsync<DecoderFallbackException>(() => store.LoadAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>Checks complete replacement, truncation of old content, and repeated saves.</summary>
    [Fact]
    public async Task ExistingFileIsReplacedAndCanBeSavedAgain()
    {
        string path = Path.Combine(directory, "existing.txt");
        await File.WriteAllTextAsync(path, "long original contents", TestContext.Current.CancellationToken);

        _ = await store.SaveAsync(path, "new", null, true, TestContext.Current.CancellationToken);
        Assert.Equal("new"u8.ToArray(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        _ = await store.SaveAsync(path, "", null, true, TestContext.Current.CancellationToken);
        Assert.Empty(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal([path], Directory.GetFileSystemEntries(directory));
    }

    /// <summary>Checks cleanup after a partial encoding failure and preserves application state.</summary>
    /// <param name="existing">Whether the failed destination already exists.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EncodingFailurePreservesFilesAndSessionAndAllowsRetry(bool existing)
    {
        string original = Path.Combine(directory, "original.txt");
        string destination = Path.Combine(directory, "save-as.txt");
        await File.WriteAllTextAsync(original, "baseline", TestContext.Current.CancellationToken);
        if (existing)
        {
            await File.WriteAllTextAsync(destination, "previous", TestContext.Current.CancellationToken);
        }

        DocumentSession session = await DocumentSession.LoadAsync(store, original, TestContext.Current.CancellationToken);
        session.Document.ReplaceText(new string('a', 16384) + "\uD800");

        _ = await Assert.ThrowsAsync<EncoderFallbackException>(() => session.SaveAsAsync(destination, true, TestContext.Current.CancellationToken));

        Assert.Equal(original, session.Destination);
        Assert.True(session.Document.IsDirty);
        Assert.Equal("baseline", await File.ReadAllTextAsync(original, TestContext.Current.CancellationToken));
        Assert.Equal(existing, File.Exists(destination));
        if (existing)
        {
            Assert.Equal("previous", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        }

        Assert.Equal(existing ? 2 : 1, Directory.GetFileSystemEntries(directory).Length);
        session.Document.ReplaceText("baseline");
        Assert.False(session.Document.IsDirty);
        session.Document.ReplaceText("retry");
        _ = await session.SaveAsAsync(destination, true, TestContext.Current.CancellationToken);
        Assert.Equal("retry", (await store.LoadAsync(destination, TestContext.Current.CancellationToken)).Text);
        Assert.Equal(destination, session.Destination);
        Assert.False(session.Document.IsDirty);
        Assert.Equal(2, Directory.GetFileSystemEntries(directory).Length);
    }

    /// <summary>Checks that a replacement failure removes the temporary file and allows retry.</summary>
    [Fact]
    public async Task DirectoryDestinationFailsWithoutChangingSession()
    {
        string destination = Directory.CreateDirectory(Path.Combine(directory, "blocked")).FullName;
        string sentinel = Path.Combine(destination, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "keep", TestContext.Current.CancellationToken);
        DocumentSession session = DocumentSession.CreateUntitled(store);
        session.Document.ReplaceText("draft");

        Exception? error = await Record.ExceptionAsync(() => session.SaveAsAsync(destination, true, TestContext.Current.CancellationToken));

        Assert.True(error is IOException or UnauthorizedAccessException, $"Unexpected error: {error}");
        Assert.Null(session.Destination);
        Assert.True(session.Document.IsDirty);
        Assert.Equal("keep", await File.ReadAllTextAsync(sentinel, TestContext.Current.CancellationToken));
        Assert.Equal([destination], Directory.GetFileSystemEntries(directory));
        Directory.Delete(destination, recursive: true);
        _ = await session.SaveAsAsync(destination, true, TestContext.Current.CancellationToken);
        Assert.Equal("draft", (await store.LoadAsync(destination, TestContext.Current.CancellationToken)).Text);
        Assert.False(session.Document.IsDirty);
    }

    /// <summary>Checks missing files and missing parent directories without creating directories.</summary>
    [Fact]
    public async Task MissingPathsPropagateActionableExceptions()
    {
        string file = Path.Combine(directory, "missing.txt");
        string nested = Path.Combine(directory, "absent", "missing.txt");
        FileNotFoundException error = await Assert.ThrowsAsync<FileNotFoundException>(() => store.LoadAsync(file, TestContext.Current.CancellationToken));
        Assert.Equal(file, error.FileName);
        _ = await Assert.ThrowsAsync<DirectoryNotFoundException>(() => store.LoadAsync(nested, TestContext.Current.CancellationToken));
        _ = await Assert.ThrowsAsync<DirectoryNotFoundException>(() => store.SaveAsync(nested, "draft", null, true, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFileSystemEntries(directory));
    }

    /// <summary>Checks permission failures on Unix hosts that enforce directory permissions for this user.</summary>
    [Fact]
    public async Task UnwritableDirectoryRejectsSaveAndAllowsRetry()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix permission checks do not apply on Windows.");
            return;
        }

        string path = Path.Combine(directory, "original.txt");
        await File.WriteAllTextAsync(path, "original", TestContext.Current.CancellationToken);
        UnixFileMode mode = File.GetUnixFileMode(directory);
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            string probe = Path.Combine(directory, "permission-probe");
            try
            {
                using FileStream stream = File.Create(probe);
                Assert.Skip("This user can bypass Unix directory permissions.");
            }
            catch (UnauthorizedAccessException)
            {
                // The host enforces the permission needed by this test.
            }

            _ = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.SaveAsync(path, "new", null, true, TestContext.Current.CancellationToken));
            Assert.Equal("original", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal([path], Directory.GetFileSystemEntries(directory));
        }
        finally
        {
            File.SetUnixFileMode(directory, mode);
        }

        _ = await store.SaveAsync(path, "retry", null, true, TestContext.Current.CancellationToken);
        Assert.Equal("retry", (await store.LoadAsync(path, TestContext.Current.CancellationToken)).Text);
    }

    /// <summary>Checks replacement failure when Windows denies delete sharing on the old file.</summary>
    [Fact]
    public async Task WindowsLockedDestinationPreservesPreviousFileAndAllowsRetry()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows delete-sharing restrictions do not apply on this host.");
            return;
        }

        string path = Path.Combine(directory, "locked.txt");
        await File.WriteAllTextAsync(path, "previous", TestContext.Current.CancellationToken);
        using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Exception? error = await Record.ExceptionAsync(() => store.SaveAsync(path, "new", null, true, TestContext.Current.CancellationToken));
            Assert.True(error is IOException or UnauthorizedAccessException, $"Unexpected error: {error}");
            Assert.Equal("previous", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal([path], Directory.GetFileSystemEntries(directory));
        }

        _ = await store.SaveAsync(path, "retry", null, true, TestContext.Current.CancellationToken);
        Assert.Equal("retry", (await store.LoadAsync(path, TestContext.Current.CancellationToken)).Text);
    }

    /// <summary>Checks invalid destination validation at the adapter boundary.</summary>
    /// <param name="destination">An invalid path.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task InvalidDestinationsFailBeforeFileAccess(string? destination)
    {
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(() => store.LoadAsync(destination!, TestContext.Current.CancellationToken));
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(() => store.SaveAsync(destination!, "text", null, true, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFileSystemEntries(directory));
    }

    /// <summary>Checks null text rejection without replacing an existing file.</summary>
    [Fact]
    public async Task NullTextDoesNotReplaceDestination()
    {
        string path = Path.Combine(directory, "original.txt");
        await File.WriteAllTextAsync(path, "original", TestContext.Current.CancellationToken);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => store.SaveAsync(path, null!, null, true, TestContext.Current.CancellationToken));

        Assert.Equal("original", (await store.LoadAsync(path, TestContext.Current.CancellationToken)).Text);
        Assert.Equal([path], Directory.GetFileSystemEntries(directory));
    }

    /// <summary>Checks cancellation before file access, including an existing destination.</summary>
    [Fact]
    public async Task PreCancelledOperationsLeaveFilesUnchanged()
    {
        string path = Path.Combine(directory, "original.txt");
        await File.WriteAllTextAsync(path, "original", TestContext.Current.CancellationToken);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.LoadAsync(path, cancellation.Token));
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(path, "new", null, true, cancellation.Token));

        Assert.Equal("original", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal([path], Directory.GetFileSystemEntries(directory));
    }

    /// <summary>Checks cancellation of a pending write, cleanup, unchanged state, and retry.</summary>
    [Fact]
    public async Task PendingSaveCancellationPreservesPreviousFileAndSession()
    {
        string original = Path.Combine(directory, "original.txt");
        string destination = Path.Combine(directory, "destination.txt");
        await File.WriteAllTextAsync(original, "baseline", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(destination, "previous", TestContext.Current.CancellationToken);
        DocumentSession session = await DocumentSession.LoadAsync(store, original, TestContext.Current.CancellationToken);
        session.Document.ReplaceText(new string('x', 32 * 1024 * 1024));
        using CancellationTokenSource cancellation = new();

        Task save = session.SaveAsAsync(destination, true, cancellation.Token);
        Assert.False(save.IsCompleted);
        await cancellation.CancelAsync();
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(original, session.Destination);
        Assert.True(session.Document.IsDirty);
        Assert.Equal("previous", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Equal("baseline", await File.ReadAllTextAsync(original, TestContext.Current.CancellationToken));
        Assert.Equal(2, Directory.GetFileSystemEntries(directory).Length);
        session.Document.ReplaceText("retry");
        _ = await session.SaveAsAsync(destination, true, TestContext.Current.CancellationToken);
        Assert.False(session.Document.IsDirty);
        Assert.Equal("retry", (await store.LoadAsync(destination, TestContext.Current.CancellationToken)).Text);
    }

    /// <summary>Checks cancellation of a pending read without modifying its input.</summary>
    [Fact]
    public async Task PendingLoadCancellationPropagates()
    {
        string path = Path.Combine(directory, "large.txt");
        await File.WriteAllTextAsync(path, new string('x', 32 * 1024 * 1024), TestContext.Current.CancellationToken);
        using CancellationTokenSource cancellation = new();

        Task<StoredDocument> load = store.LoadAsync(path, cancellation.Token);
        Assert.False(load.IsCompleted);
        await cancellation.CancelAsync();
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(32 * 1024 * 1024, new FileInfo(path).Length);
        Assert.Equal([path], Directory.GetFileSystemEntries(directory));
    }

    /// <summary>Deletes only the directory created by this test instance.</summary>
    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
    }
}

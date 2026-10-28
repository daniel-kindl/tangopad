using Editor.Application;
using Xunit;

namespace Editor.Infrastructure.Tests;

/// <summary>Checks that a save keeps the file mode and links and reaches the disk in a safe order.</summary>
public sealed class SafeSaveTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("tangopad-safe-save-").FullName;
    private readonly FileTextDocumentStore store = new();

    /// <summary>Checks that a save keeps the Unix permission bits of the existing file.</summary>
    /// <param name="mode">The octal file mode of the existing file.</param>
    [Theory]
    [InlineData(0b110_000_000)]
    [InlineData(0b110_100_100)]
    [InlineData(0b111_101_101)]
    public async Task SaveKeepsTheUnixFileMode(int mode)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes do not apply on Windows.");
            return;
        }

        string path = Path.Combine(directory, "mode.txt");
        await File.WriteAllTextAsync(path, "old", TestContext.Current.CancellationToken);
        File.SetUnixFileMode(path, (UnixFileMode)mode);

        _ = await store.SaveAsync(path, "new", null, true, TestContext.Current.CancellationToken);

        Assert.Equal((UnixFileMode)mode, File.GetUnixFileMode(path));
        Assert.Equal("new", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>Checks that a save removes the setuid, setgid and sticky bits and keeps the permission bits.</summary>
    /// <param name="mode">The octal file mode of the existing file.</param>
    /// <param name="expected">The octal file mode after the save.</param>
    [Theory]
    [InlineData(0b100_111_101_101, 0b111_101_101)]
    [InlineData(0b010_111_101_101, 0b111_101_101)]
    [InlineData(0b001_111_101_101, 0b111_101_101)]
    [InlineData(0b111_111_111_111, 0b111_111_111)]
    public async Task SaveRemovesSetuidSetgidAndSticky(int mode, int expected)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes do not apply on Windows.");
            return;
        }

        string path = Path.Combine(directory, "special.txt");
        await File.WriteAllTextAsync(path, "old", TestContext.Current.CancellationToken);
        File.SetUnixFileMode(path, (UnixFileMode)mode);
        if (File.GetUnixFileMode(path) != (UnixFileMode)mode)
        {
            Assert.Skip("This file system does not keep the special mode bits.");
            return;
        }

        _ = await store.SaveAsync(path, "new", null, true, TestContext.Current.CancellationToken);

        Assert.Equal((UnixFileMode)expected, File.GetUnixFileMode(path));
        Assert.Equal("new", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>Checks that a save through a relative link or a chain of links uses the folder of the final target.</summary>
    /// <param name="chain">Whether the saved link points to a second link.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveThroughRelativeLinkUsesTheFolderOfTheFinalTarget(bool chain)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows keeps the previous save path. Links are followed only on Unix.");
            return;
        }

        string real = Directory.CreateDirectory(Path.Combine(directory, "real")).FullName;
        string middle = Directory.CreateDirectory(Path.Combine(directory, "middle")).FullName;
        string links = Directory.CreateDirectory(Path.Combine(directory, "links")).FullName;
        string target = Path.Combine(real, "target.txt");
        string link = Path.Combine(links, "link.txt");
        await File.WriteAllTextAsync(target, "old", TestContext.Current.CancellationToken);
        string next = Path.Combine(middle, "next.txt");
        string first = chain ? "../middle/next.txt" : "../real/target.txt";
        if (chain)
        {
            _ = File.CreateSymbolicLink(next, "../real/target.txt");
        }

        _ = File.CreateSymbolicLink(link, first);

        RecordingSync sync = new(target);
        string? revision = await new FileTextDocumentStore(sync).SaveAsync(link, "new", null, true, TestContext.Current.CancellationToken);

        Assert.Equal(first, new FileInfo(link).LinkTarget);
        if (chain)
        {
            Assert.Equal("../real/target.txt", new FileInfo(next).LinkTarget);
        }

        Assert.Equal("new", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        Assert.Equal([target], Directory.GetFileSystemEntries(real));
        Assert.Equal([link], Directory.GetFileSystemEntries(links));
        Assert.Equal(chain ? [next] : [], Directory.GetFileSystemEntries(middle));
        Assert.Equal(["file old 3", "folder " + real + " new"], sync.Events);
        Assert.StartsWith(link + "\n", revision, StringComparison.Ordinal);
    }

    /// <summary>Checks that a failed folder flush after the rename keeps the new content and the modified state.</summary>
    [Fact]
    public async Task FolderFlushFailureAfterRenameSaysTheFileWasSaved()
    {
        string path = Path.Combine(directory, "flush.txt");
        await File.WriteAllTextAsync(path, "old", TestContext.Current.CancellationToken);
        IOException cause = new("disk");
        FileTextDocumentStore failing = new(new FailingFolderSync(cause));
        DocumentSession session = await DocumentSession.LoadAsync(failing, path, TestContext.Current.CancellationToken);
        session.Document.ReplaceText("new");

        IOException failure = await Assert.ThrowsAsync<IOException>(() => session.SaveAsync(TestContext.Current.CancellationToken));

        Assert.Equal(FileTextDocumentStore.SavedButNotFlushedMessage, failure.Message);
        Assert.Equal("The file was saved, but it could not be flushed to disk. It may not survive a power loss. Save again.", failure.Message);
        Assert.Same(cause, failure.InnerException);
        Assert.Equal("new", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal([path], Directory.GetFileSystemEntries(directory));
        Assert.True(session.Document.IsDirty);
        Assert.Equal(path, session.Destination);
    }

    /// <summary>Checks that the default sync opens only folders, with the flags of the current system.</summary>
    [Fact]
    public async Task DefaultSyncOpensOnlyFolders()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows has no folder flush.");
            return;
        }

        int expected = OperatingSystem.IsMacOS() ? 0x0010_0000 | 0x0100_0000 : 0x0001_0000 | 0x0008_0000;
        Assert.Equal(expected, FileSync.DirectoryOpenFlags);
        string file = Path.Combine(directory, "file.txt");
        await File.WriteAllTextAsync(file, "text", TestContext.Current.CancellationToken);

        _ = Assert.ThrowsAny<IOException>(() => FileSync.Default.FlushDirectory(file));
    }

    /// <summary>Checks that a save through a symbolic link keeps the link and changes the target file.</summary>
    [Fact]
    public async Task SaveThroughSymbolicLinkChangesTheTarget()
    {
        string folder = Directory.CreateDirectory(Path.Combine(directory, "real")).FullName;
        string target = Path.Combine(folder, "target.txt");
        string link = Path.Combine(directory, "link.txt");
        await File.WriteAllTextAsync(target, "old", TestContext.Current.CancellationToken);
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows keeps the previous save path. Links are followed only on Unix.");
            return;
        }

        _ = File.CreateSymbolicLink(link, target);

        RecordingSync sync = new(target);
        string? revision = await new FileTextDocumentStore(sync).SaveAsync(link, "new", null, true, TestContext.Current.CancellationToken);

        Assert.Equal(target, new FileInfo(link).LinkTarget);
        Assert.Equal("new", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        Assert.Equal([target], Directory.GetFileSystemEntries(folder));
        Assert.Equal(["file old 3", "folder " + folder + " new"], sync.Events);
        Assert.StartsWith(link + "\n", revision, StringComparison.Ordinal);
    }

    /// <summary>Checks that a save into a read-only folder fails and does not write the file in place.</summary>
    [Fact]
    public async Task SaveIntoReadOnlyFolderFailsAndKeepsTheOriginal()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix folder modes do not apply on Windows.");
            return;
        }

        string folder = Directory.CreateDirectory(Path.Combine(directory, "locked")).FullName;
        string path = Path.Combine(folder, "original.txt");
        await File.WriteAllTextAsync(path, "old", TestContext.Current.CancellationToken);
        File.SetUnixFileMode(path, (UnixFileMode)0b110_100_100);
        byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        File.SetUnixFileMode(folder, (UnixFileMode)0b101_101_101);
        try
        {
            if (IsFolderWritable(folder))
            {
                Assert.Skip("This user can write to read-only folders.");
                return;
            }

            Exception failure = await Assert.ThrowsAnyAsync<Exception>(
                () => store.SaveAsync(path, "new", null, true, TestContext.Current.CancellationToken));

            Assert.True(failure is UnauthorizedAccessException or IOException, failure.ToString());
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal((UnixFileMode)0b110_100_100, File.GetUnixFileMode(path));
            Assert.Equal([path], Directory.GetFileSystemEntries(folder));
        }
        finally
        {
            File.SetUnixFileMode(folder, (UnixFileMode)0b111_000_000);
        }
    }

    /// <summary>Checks that the temporary file reaches the disk before the rename and the folder after it.</summary>
    /// <param name="existing">Whether the destination already exists.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveFlushesTheFileThenRenamesThenFlushesTheFolder(bool existing)
    {
        string path = Path.Combine(directory, "ordered.txt");
        if (existing)
        {
            await File.WriteAllTextAsync(path, "old", TestContext.Current.CancellationToken);
        }

        RecordingSync sync = new(path);
        _ = await new FileTextDocumentStore(sync).SaveAsync(path, "new", null, true, TestContext.Current.CancellationToken);

        Assert.Equal([(existing ? "file old" : "file missing") + " 3", "folder " + directory + " new"], sync.Events);
        Assert.Equal([path], Directory.GetFileSystemEntries(directory));
    }

    /// <summary>Checks that the default sync flushes a folder on Unix and does nothing on Windows.</summary>
    [Fact]
    public void DefaultSyncFlushesFoldersOnlyOnUnix()
    {
        string missing = Path.Combine(directory, "missing");

        FileSync.Default.FlushDirectory(directory);

        if (OperatingSystem.IsWindows())
        {
            FileSync.Default.FlushDirectory(missing);
        }
        else
        {
            _ = Assert.ThrowsAny<IOException>(() => FileSync.Default.FlushDirectory(missing));
        }
    }

    private static bool IsFolderWritable(string folder)
    {
        string probe = Path.Combine(folder, ".probe");
        try
        {
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Deletes only the directory created by this test instance.</summary>
    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
    }

    /// <summary>Flushes files and fails each folder flush.</summary>
    private sealed class FailingFolderSync(IOException failure) : IFileSync
    {
        public void FlushFile(FileStream stream)
        {
            stream.Flush(flushToDisk: true);
        }

        public void FlushDirectory(string path)
        {
            throw failure;
        }
    }

    /// <summary>Records each flush with the state of the destination at that time.</summary>
    private sealed class RecordingSync(string destination) : IFileSync
    {
        public List<string> Events { get; } = [];

        public void FlushFile(FileStream stream)
        {
            Assert.NotEqual(destination, stream.Name);
            Assert.Equal(Path.GetDirectoryName(destination), Path.GetDirectoryName(stream.Name));
            Events.Add($"file {State()} {stream.Length}");
            stream.Flush(flushToDisk: true);
        }

        public void FlushDirectory(string path)
        {
            Events.Add($"folder {path} {State()}");
        }

        private string State()
        {
            return File.Exists(destination) ? File.ReadAllText(destination) : "missing";
        }
    }
}

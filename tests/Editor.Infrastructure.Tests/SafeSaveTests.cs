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

    /// <summary>Checks that a save through a symbolic link keeps the link and changes the target file.</summary>
    [Fact]
    public async Task SaveThroughSymbolicLinkChangesTheTarget()
    {
        string folder = Directory.CreateDirectory(Path.Combine(directory, "real")).FullName;
        string target = Path.Combine(folder, "target.txt");
        string link = Path.Combine(directory, "link.txt");
        await File.WriteAllTextAsync(target, "old", TestContext.Current.CancellationToken);
        try
        {
            _ = File.CreateSymbolicLink(link, target);
        }
        catch (Exception error) when (OperatingSystem.IsWindows() && error is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("This Windows user cannot create symbolic links.");
            return;
        }

        RecordingSync sync = new(target);
        string? revision = await new FileTextDocumentStore(sync).SaveAsync(link, "new", null, true, TestContext.Current.CancellationToken);

        Assert.Equal(target, new FileInfo(link).LinkTarget);
        Assert.Equal("new", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        Assert.Equal([target], Directory.GetFileSystemEntries(folder));
        Assert.Equal(["file old 3", "folder " + folder + " new"], sync.Events);
        Assert.StartsWith(link + "\n", revision, StringComparison.Ordinal);
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

    /// <summary>Deletes only the directory created by this test instance.</summary>
    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
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

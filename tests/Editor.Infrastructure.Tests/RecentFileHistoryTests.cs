using System.Text.Json;
using Xunit;

namespace Editor.Infrastructure.Tests;

/// <summary>Checks bounded path history and real settings storage.</summary>
public sealed class RecentFileHistoryTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("tangopad-recent-").FullName;

    /// <summary>Paths normalize, deduplicate, move to the front, and retain only ten entries.</summary>
    [Fact]
    public void OrderingNormalizationAndBounds()
    {
        RecentFileHistory history = new();
        for (int index = 0; index < 12; index++)
        {
            history.Record(Path.Combine(directory, $"{index}.txt"));
        }

        history.Record(Path.Combine(directory, "sub", "..", "5.txt"));
        string[] expected = ["5.txt", "11.txt", "10.txt", "9.txt", "8.txt", "7.txt", "6.txt", "4.txt", "3.txt", "2.txt"];
        Assert.Equal(expected.Select(name => Path.Combine(directory, name)), history.Paths);
        history.Record("\0invalid");
        Assert.Equal(10, history.Paths.Count);
        Assert.Equal(Path.Combine(directory, "5.txt"), history.Paths[0]);
        RecentFileHistory relative = new();
        relative.Record("./recent.txt");
        Assert.Equal([Path.Combine(Environment.CurrentDirectory, "recent.txt")], relative.Paths);
    }

    /// <summary>Comparison follows the platform and does not discard case-distinct Unix files.</summary>
    [Fact]
    public void PathCaseUsesPlatformComparison()
    {
        RecentFileHistory history = new();
        history.Record(Path.Combine(directory, "name.txt"));
        history.Record(Path.Combine(directory, "NAME.txt"));
        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 2, history.Paths.Count);
        Assert.Equal(Path.Combine(directory, "NAME.txt"), history.Paths[0]);
    }

    /// <summary>Only the version and path list persist, and clear survives another session.</summary>
    [Fact]
    public async Task PersistenceContainsOnlyPathsAndClearSurvivesRestart()
    {
        string settings = Path.Combine(directory, "settings", "recent-files.json");
        RecentFileHistory history = new(settings);
        await history.Ready.ConfigureAwait(true);
        history.Record(Path.Combine(directory, "λ.txt"));
        history.Record(Path.Combine(directory, "other", "λ.txt"));
        await history.FlushAsync().ConfigureAwait(true);
        using (JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(settings, TestContext.Current.CancellationToken)))
        {
            Assert.Equal(["version", "paths"], json.RootElement.EnumerateObject().Select(property => property.Name));
            Assert.Equal(1, json.RootElement.GetProperty("version").GetInt32());
        }

        RecentFileHistory restored = new(settings);
        await restored.Ready.ConfigureAwait(true);
        Assert.Equal([Path.Combine(directory, "other", "λ.txt"), Path.Combine(directory, "λ.txt")], restored.Paths);
        restored.Clear();
        await restored.FlushAsync().ConfigureAwait(true);
        RecentFileHistory cleared = new(settings);
        await cleared.Ready.ConfigureAwait(true);
        Assert.Empty(cleared.Paths);
        Assert.Equal([settings], Directory.GetFiles(Path.GetDirectoryName(settings)!));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(settings));
        }
    }

    /// <summary>Corrupt or unsupported settings do not prevent later history updates.</summary>
    /// <param name="contents">The invalid settings contents.</param>
    [Theory]
    [InlineData("broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData(/*lang=json,strict*/ "{\"version\":2,\"paths\":[]}")]
    [InlineData(/*lang=json,strict*/ "{\"version\":\"1\",\"paths\":[]}")]
    [InlineData(/*lang=json,strict*/ "{\"version\":1,\"paths\":null}")]
    [InlineData(/*lang=json,strict*/ "{\"version\":1,\"paths\":[null,17,\"relative.txt\",\"\"]}")]
    public async Task CorruptStorageIsIgnored(string contents)
    {
        string settings = Path.Combine(directory, "recent-files.json");
        await File.WriteAllTextAsync(settings, contents, TestContext.Current.CancellationToken);
        RecentFileHistory history = new(settings);
        await history.Ready.ConfigureAwait(true);
        Assert.Empty(history.Paths);
        history.Record(Path.Combine(directory, "valid.md"));
        await history.FlushAsync().ConfigureAwait(true);
        RecentFileHistory restored = new(settings);
        await restored.Ready.ConfigureAwait(true);
        Assert.Equal([Path.Combine(directory, "valid.md")], restored.Paths);
    }

    /// <summary>An unavailable settings directory does not prevent memory-only history or clearing.</summary>
    [Fact]
    public async Task UnavailableStorageDoesNotFailHistoryOperations()
    {
        string blocker = Path.Combine(directory, "file");
        await File.WriteAllTextAsync(blocker, "keep", TestContext.Current.CancellationToken);
        RecentFileHistory history = new(Path.Combine(blocker, "recent-files.json"));
        await history.Ready.ConfigureAwait(true);
        history.Record(Path.Combine(directory, "document.txt"));
        await history.FlushAsync().ConfigureAwait(true);
        Assert.Equal([Path.Combine(directory, "document.txt")], history.Paths);
        history.Clear();
        await history.FlushAsync().ConfigureAwait(true);
        Assert.Empty(history.Paths);
        Assert.True(history.StorageFailed);
        Assert.Equal("keep", await File.ReadAllTextAsync(blocker, TestContext.Current.CancellationToken));
    }

    /// <summary>Clear during initial loading cannot restore the previous list.</summary>
    [Fact]
    public async Task EarlyClearAndUpdatesSurviveInitialization()
    {
        string settings = Path.Combine(directory, "recent-files.json");
        RecentFileHistory first = new(settings);
        first.Record(Path.Combine(directory, "old.txt"));
        await first.FlushAsync().ConfigureAwait(true);
        RecentFileHistory next = new(settings);
        next.Clear();
        next.Record(Path.Combine(directory, "new.txt"));
        await next.FlushAsync().ConfigureAwait(true);
        Assert.Equal([Path.Combine(directory, "new.txt")], next.Paths);
        RecentFileHistory restored = new(settings);
        await restored.Ready.ConfigureAwait(true);
        Assert.Equal([Path.Combine(directory, "new.txt")], restored.Paths);
    }

    /// <summary>Early updates merge ahead of stored paths and persist the merged list.</summary>
    [Fact]
    public async Task EarlyRecordMergesLoadedHistory()
    {
        string settings = Path.Combine(directory, "recent-files.json");
        string old = Path.Combine(directory, "old.txt");
        string recent = Path.Combine(directory, "new.txt");
        RecentFileHistory first = new(settings);
        first.Record(old);
        await first.FlushAsync().ConfigureAwait(true);
        RecentFileHistory next = new(settings);
        next.Record(recent);
        await next.FlushAsync().ConfigureAwait(true);
        Assert.Equal([recent, old], next.Paths);
        RecentFileHistory restored = new(settings);
        await restored.Ready.ConfigureAwait(true);
        Assert.Equal([recent, old], restored.Paths);
    }

    /// <summary>Large settings files are ignored without blocking later writes.</summary>
    [Fact]
    public async Task OversizedStorageIsIgnored()
    {
        string settings = Path.Combine(directory, "recent-files.json");
        await File.WriteAllTextAsync(settings, new string(' ', 65537), TestContext.Current.CancellationToken);
        RecentFileHistory history = new(settings);
        await history.Ready.ConfigureAwait(true);
        Assert.Empty(history.Paths);
        history.Record(Path.Combine(directory, "new.txt"));
        await history.FlushAsync().ConfigureAwait(true);
        Assert.False(history.StorageFailed);
        RecentFileHistory restored = new(settings);
        await restored.Ready.ConfigureAwait(true);
        Assert.Equal([Path.Combine(directory, "new.txt")], restored.Paths);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
    }
}

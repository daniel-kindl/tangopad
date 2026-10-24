using Xunit;

namespace Editor.Infrastructure.Tests;

/// <summary>Checks preference storage through isolated local files.</summary>
public sealed class EditorPreferencesTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    /// <summary>Early changes survive loading and a new storage instance restores them.</summary>
    [Fact]
    public async Task ChangesSurviveRelaunch()
    {
        _ = Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "state.bin");
        await File.WriteAllTextAsync(Path.Combine(directory, "editor-preferences.json"), /*lang=json,strict*/ "{\"version\":1,\"wordWrap\":false,\"showLineNumbers\":false,\"fontSize\":24}", TestContext.Current.CancellationToken);
        EditorPreferences preferences = new AppStateStore(path, directory).Preferences;
        preferences.Update(wordWrap: true);
        await preferences.FlushAsync().ConfigureAwait(true);
        EditorPreferences restored = new(path);
        await restored.Ready.ConfigureAwait(true);
        Assert.True(restored.Current.WordWrap);
        Assert.False(restored.Current.ShowLineNumbers);
        Assert.Equal(24, restored.Current.FontSize);
    }

    /// <summary>Invalid legacy storage preserves the source and blocks migration.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData(/*lang=json,strict*/ "{\"version\":2,\"wordWrap\":true}")]
    [InlineData(/*lang=json,strict*/ "{\"version\":\"1\"}")]
    [InlineData(/*lang=json,strict*/ "{\"version\":1e99}")]
    public async Task InvalidStorageUsesDefaults(string json)
    {
        _ = Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "state.bin");
        await File.WriteAllTextAsync(Path.Combine(directory, "editor-preferences.json"), json, TestContext.Current.CancellationToken);
        EditorPreferences preferences = new AppStateStore(path, directory).Preferences;
        await preferences.Ready.ConfigureAwait(true);
        Assert.Equal(new EditorPresentation(false, true, 15), preferences.Current);
        preferences.Update(wordWrap: true);
        await preferences.FlushAsync().ConfigureAwait(true);
        Assert.True(preferences.StorageFailed);
        Assert.False(File.Exists(path));
        Assert.Equal(json, await File.ReadAllTextAsync(Path.Combine(directory, "editor-preferences.json"), TestContext.Current.CancellationToken));
    }

    /// <summary>Missing and oversized files use defaults.</summary>
    [Fact]
    public async Task MissingAndOversizedStorageUseDefaults()
    {
        _ = Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "state.bin");
        EditorPreferences missing = new(path);
        await missing.Ready.ConfigureAwait(true);
        Assert.Equal(new EditorPresentation(false, true, 15), missing.Current);
        await File.WriteAllTextAsync(Path.Combine(directory, "editor-preferences.json"), new string(' ', 4097), TestContext.Current.CancellationToken);
        EditorPreferences oversized = new AppStateStore(path, directory).Preferences;
        await oversized.Ready.ConfigureAwait(true);
        Assert.Equal(new EditorPresentation(false, true, 15), oversized.Current);
    }

    /// <summary>Loaded sizes are bounded and invalid keys retain defaults.</summary>
    [Theory]
    [InlineData("-1", 8)]
    [InlineData("999", 48)]
    [InlineData("1e999", 15)]
    [InlineData("null", 15)]
    [InlineData("18.5", 18.5)]
    public async Task FontBoundsAndMissingKeys(string font, double expected)
    {
        _ = Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "state.bin");
        await File.WriteAllTextAsync(Path.Combine(directory, "editor-preferences.json"), "{\"version\":1,\"fontSize\":" + font + ",\"wordWrap\":0,\"extra\":true}", TestContext.Current.CancellationToken);
        EditorPreferences preferences = new AppStateStore(path, directory).Preferences;
        await preferences.Ready.ConfigureAwait(true);
        Assert.Equal(new EditorPresentation(false, true, expected), preferences.Current);
    }

    /// <summary>Concurrent updates preserve independent fields and flush the last change.</summary>
    [Fact]
    public async Task ConcurrentUpdatesAndRedundantWrites()
    {
        _ = Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "state.bin");
        EditorPreferences preferences = new AppStateStore(path, directory).Preferences;
        await preferences.Ready.ConfigureAwait(true);
        await Task.WhenAll(Task.Run(() => preferences.Update(wordWrap: true), TestContext.Current.CancellationToken),
            Task.Run(() => preferences.Update(showLineNumbers: false), TestContext.Current.CancellationToken),
            Task.Run(() => preferences.Update(fontSize: 99), TestContext.Current.CancellationToken)).ConfigureAwait(true);
        await preferences.FlushAsync().ConfigureAwait(true);
        File.SetLastWriteTimeUtc(path, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        DateTime written = File.GetLastWriteTimeUtc(path);
        preferences.Update(fontSize: 48);
        await preferences.FlushAsync().ConfigureAwait(true);
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
        EditorPreferences restored = new(path);
        await restored.Ready.ConfigureAwait(true);
        Assert.Equal(new EditorPresentation(true, false, 48), restored.Current);
        Assert.Equal(["state.bin"], Directory.GetFiles(directory).Select(Path.GetFileName));
        Assert.Equal(new EditorPresentation(true, false, 48), BinaryAppState.Decode(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Presentation);
    }

    /// <summary>Storage failures preserve choices and permit retry.</summary>
    [Fact]
    public async Task StorageFailurePermitsRetry()
    {
        _ = Directory.CreateDirectory(directory);
        string blocker = Path.Combine(directory, "blocker");
        await File.WriteAllTextAsync(blocker, "keep", TestContext.Current.CancellationToken);
        EditorPreferences preferences = new(Path.Combine(blocker, "state.bin"));
        preferences.Update(wordWrap: true, fontSize: double.NaN);
        await preferences.FlushAsync().ConfigureAwait(true);
        Assert.True(preferences.StorageFailed);
        Assert.Equal(new EditorPresentation(true, true, 15), preferences.Current);
        Assert.Equal("keep", await File.ReadAllTextAsync(blocker, TestContext.Current.CancellationToken));
        File.Delete(blocker);
        preferences.Update(wordWrap: true);
        await preferences.FlushAsync().ConfigureAwait(true);
        Assert.False(preferences.StorageFailed);
        EditorPreferences restored = new(Path.Combine(blocker, "state.bin"));
        await restored.Ready.ConfigureAwait(true);
        Assert.True(restored.Current.WordWrap);
    }

    /// <summary>A failed replacement preserves existing data and cleans up temporary files.</summary>
    [Fact]
    public async Task FailedReplacementPreservesDestination()
    {
        string path = Path.Combine(directory, "state.bin");
        _ = Directory.CreateDirectory(path);
        string marker = Path.Combine(path, "keep.txt");
        await File.WriteAllTextAsync(marker, "keep", TestContext.Current.CancellationToken);
        EditorPreferences preferences = new AppStateStore(path, directory).Preferences;
        preferences.Update(showLineNumbers: false);
        await preferences.FlushAsync().ConfigureAwait(true);
        Assert.True(preferences.StorageFailed);
        Assert.Equal("keep", await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(directory));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}

using System.Buffers.Binary;
using System.Security.Cryptography;
using Xunit;

namespace Editor.Infrastructure.Tests;

/// <summary>Checks the binary schema, shared writer, migration, and recovery through real files.</summary>
public sealed class AppStateStoreTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("tangopad-state-").FullName;

    /// <summary>All fields and Unicode paths survive interleaved writes and restart.</summary>
    [Fact]
    public async Task SharedWriterPreservesEveryField()
    {
        string path = Path.Combine(directory, "state.bin");
        AppStateStore store = new(path);
        string recent = Path.Combine(directory, "λ-😀.txt");
        store.Preferences.Update(wordWrap: true, showLineNumbers: false, fontSize: 18.5);
        store.History.Record(recent);
        await Task.WhenAll(store.Preferences.FlushAsync(), store.History.FlushAsync()).ConfigureAwait(true);
        AppStateStore restored = new(path);
        await Task.WhenAll(restored.Preferences.Ready, restored.History.Ready).ConfigureAwait(true);
        Assert.Equal(new EditorPresentation(true, false, 18.5), restored.Preferences.Current);
        Assert.Equal([recent], restored.History.Paths);
        Assert.Null(restored.StorageError);
        byte[] expected = new BinaryAppState(new(true, false, 18.5), [recent]).Encode();
        Assert.Equal(expected, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>Default state has a deterministic header, size, and empty history.</summary>
    [Fact]
    public void EmptyStateRoundTrips()
    {
        byte[] bytes = new BinaryAppState(new(), []).Encode();
        Assert.Equal(58, bytes.Length);
        Assert.Equal("TANGOST\0"u8.ToArray(), bytes[..8]);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));
        BinaryAppState restored = BinaryAppState.Decode(bytes);
        Assert.Equal(new EditorPresentation(false, true, 15), restored.Presentation);
        Assert.Empty(restored.Paths);
    }

    /// <summary>Every truncation and invalid field is rejected before state is used.</summary>
    [Fact]
    public void RejectsTruncationAndInvalidFields()
    {
        byte[] valid = new BinaryAppState(new(), [Path.Combine(directory, "λ.txt")]).Encode();
        for (int length = 0; length < valid.Length; length++)
        {
            _ = Assert.Throws<InvalidDataException>(() => BinaryAppState.Decode(valid[..length]));
        }

        foreach (int offset in new[] { 0, 8, 12, 13, 21, 22, 26 })
        {
            byte[] corrupt = (byte[])valid.Clone();
            corrupt[offset] = 255;
            SHA256.HashData(corrupt.AsSpan(0, corrupt.Length - 32)).CopyTo(corrupt, corrupt.Length - 32);
            _ = Assert.Throws<InvalidDataException>(() => BinaryAppState.Decode(corrupt));
        }
        valid[^1] ^= 1;
        _ = Assert.Throws<InvalidDataException>(() => BinaryAppState.Decode(valid));
        _ = Assert.Throws<InvalidDataException>(() => BinaryAppState.Decode(new byte[65537]));
    }

    /// <summary>Corrupt and future files remain byte-identical after preference and history changes.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedStateIsProtected(bool future)
    {
        string path = Path.Combine(directory, "state.bin");
        byte[] bytes = new BinaryAppState(new(true), []).Encode();
        bytes[8] = future ? (byte)2 : (byte)0;
        if (future) { SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes, bytes.Length - 32); }
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        AppStateStore store = new(path, directory);
        store.History.Record(Path.Combine(directory, "new.txt"));
        store.Preferences.Update(fontSize: 24);
        await Task.WhenAll(store.History.FlushAsync(), store.Preferences.FlushAsync()).ConfigureAwait(true);
        Assert.NotNull(store.StorageError);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(24, store.Preferences.Current.FontSize);
    }

    /// <summary>Both legacy files migrate together, stay intact, and cannot reappear after Clear.</summary>
    [Fact]
    public async Task MigrationIsNonDestructiveAndIdempotent()
    {
        string preferences = Path.Combine(directory, "editor-preferences.json");
        string history = Path.Combine(directory, "recent-files.json");
        string recent = Path.Combine(directory, "λ.txt");
        const string original = /*lang=json,strict*/ "{\"version\":1,\"wordWrap\":true,\"showLineNumbers\":false,\"fontSize\":22}";
        await File.WriteAllTextAsync(preferences, original, TestContext.Current.CancellationToken);
        string oldHistory = System.Text.Json.JsonSerializer.Serialize(new { version = 1, paths = new[] { recent, recent } });
        await File.WriteAllTextAsync(history, oldHistory, TestContext.Current.CancellationToken);
        string path = Path.Combine(directory, "state.bin");
        AppStateStore store = new(path, directory);
        await Task.WhenAll(store.History.Ready, store.Preferences.Ready).ConfigureAwait(true);
        Assert.Null(store.StorageError);
        Assert.Equal(new EditorPresentation(true, false, 22), store.Preferences.Current);
        Assert.Equal([recent], store.History.Paths);
        store.History.Clear();
        await store.History.FlushAsync().ConfigureAwait(true);
        AppStateStore restored = new(path, directory);
        await restored.History.Ready.ConfigureAwait(true);
        Assert.Empty(restored.History.Paths);
        Assert.Equal(original, await File.ReadAllTextAsync(preferences, TestContext.Current.CancellationToken));
        Assert.Equal(oldHistory, await File.ReadAllTextAsync(history, TestContext.Current.CancellationToken));
    }

    /// <summary>An oversized path cannot replace a valid file, and abandoned temporary files are ignored.</summary>
    [Fact]
    public async Task FailedWritePreservesLastGoodState()
    {
        string path = Path.Combine(directory, "state.bin");
        AppStateStore store = new(path);
        store.Preferences.Update(wordWrap: true);
        await store.Preferences.FlushAsync().ConfigureAwait(true);
        byte[] original = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(directory, ".state-interrupted.tmp"), [1, 2], TestContext.Current.CancellationToken);
        store.History.Record(Path.Combine(directory, new string('a', 4097)));
        await store.History.FlushAsync().ConfigureAwait(true);
        Assert.NotNull(store.StorageError);
        Assert.Equal(original, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        AppStateStore restored = new(path);
        await restored.Preferences.Ready.ConfigureAwait(true);
        Assert.True(restored.Preferences.Current.WordWrap);
        store.History.Clear();
        await store.History.FlushAsync().ConfigureAwait(true);
        Assert.Null(store.StorageError);
    }

    /// <summary>A read-only portable directory reports an error without creating installed state.</summary>
    [Fact]
    public async Task ReadOnlyDirectoryReportsFailure()
    {
        if (OperatingSystem.IsWindows()) { return; }
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            AppStateStore store = new(Path.Combine(directory, "state.bin"), directory);
            await store.Ready.ConfigureAwait(true);
            Assert.NotNull(store.StorageError);
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally { File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    /// <summary>A failed migration write retains legacy bytes and succeeds on the next launch.</summary>
    [Fact]
    public async Task FailedMigrationCanRetryWithoutDataLoss()
    {
        if (OperatingSystem.IsWindows()) { return; }
        string legacy = Path.Combine(directory, "editor-preferences.json");
        const string original = /*lang=json,strict*/ "{\"version\":1,\"wordWrap\":true}";
        await File.WriteAllTextAsync(legacy, original, TestContext.Current.CancellationToken);
        string path = Path.Combine(directory, "state.bin");
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            AppStateStore failed = new(path, directory);
            await failed.Ready.ConfigureAwait(true);
            Assert.NotNull(failed.StorageError);
            Assert.False(File.Exists(path));
            Assert.Equal(original, await File.ReadAllTextAsync(legacy, TestContext.Current.CancellationToken));
        }
        finally { File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        AppStateStore retry = new(path, directory);
        await retry.Preferences.Ready.ConfigureAwait(true);
        Assert.True(retry.Preferences.Current.WordWrap);
        Assert.Null(retry.StorageError);
        Assert.Equal(original, await File.ReadAllTextAsync(legacy, TestContext.Current.CancellationToken));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
    }
}

using Editor.Core;
using Xunit;

namespace Editor.Application.Tests;

/// <summary>Checks plain-text persistence through the application boundary.</summary>
public sealed class DocumentSessionTests
{
    /// <summary>Checks exact load and save contents, including empty text.</summary>
    /// <param name="text">The exact document contents.</param>
    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("Příliš 🐈\r\n漢字\nالعربية\r\t\0e\u0301")]
    public async Task LoadAndSavePreserveExactText(string text)
    {
        MemoryStore store = new() { Text = text };
        using CancellationTokenSource cancellation = new();
        DocumentSession session = await DocumentSession.LoadAsync(store, "original.txt", cancellation.Token);
        Guid identity = session.Document.Id;

        Assert.NotEqual(Guid.Empty, identity);
        Assert.Equal(text, session.Document.Text);
        Assert.False(session.Document.IsDirty);
        Assert.Equal("original.txt", session.Destination);
        Assert.Equal(cancellation.Token, store.LoadToken);
        Assert.Equal("original.txt", store.LoadDestination);

        await session.SaveAsync(cancellation.Token);

        Assert.Equal(("original.txt", text), Assert.Single(store.Writes));
        Assert.Equal(cancellation.Token, store.SaveToken);
        Assert.False(session.Document.IsDirty);
        Assert.Equal(identity, session.Document.Id);
        Assert.Equal(text, session.Document.Text);
    }

    /// <summary>Checks that save-as establishes a destination without replacing identity.</summary>
    [Fact]
    public async Task UntitledDocumentRequiresSaveAsEvenWhenClean()
    {
        MemoryStore store = new();
        DocumentSession session = DocumentSession.CreateUntitled(store);
        Guid identity = session.Document.Id;

        Assert.Null(session.Destination);
        Assert.Equal(DocumentFormat.PlainText, session.Format);
        Assert.False(session.Document.IsDirty);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveAsync(TestContext.Current.CancellationToken));
        Assert.Empty(store.Writes);

        await session.SaveAsAsync("first.txt", TestContext.Current.CancellationToken);
        session.Document.ReplaceText("draft");
        await session.SaveAsAsync("second.txt", TestContext.Current.CancellationToken);
        session.Document.ReplaceText("final");
        await session.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { ("first.txt", ""), ("second.txt", "draft"), ("second.txt", "final") }, store.Writes);
        Assert.Equal("second.txt", session.Destination);
        Assert.Equal(identity, session.Document.Id);
        Assert.False(session.Document.IsDirty);
    }

    /// <summary>Checks that pending persistence acknowledges only the captured text.</summary>
    [Fact]
    public async Task PendingSavePreservesNewerEditsAndRejectsOverlappingSaves()
    {
        TaskCompletionSource completion = new();
        MemoryStore store = new();
        DocumentSession session = await DocumentSession.LoadAsync(store, "old.txt", TestContext.Current.CancellationToken);
        Guid identity = session.Document.Id;
        session.Document.ReplaceText("snapshot");
        store.Save = _ => completion.Task;

        Task save = session.SaveAsAsync("new.md", TestContext.Current.CancellationToken);
        Assert.False(save.IsCompleted);
        Assert.Equal("old.txt", session.Destination);
        Assert.Equal(DocumentFormat.PlainText, session.Format);
        session.Document.ReplaceText("newer edit");
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveAsync(TestContext.Current.CancellationToken));
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveAsAsync("overlap.txt", TestContext.Current.CancellationToken));
        Assert.Equal(("new.md", "snapshot"), Assert.Single(store.Writes));

        completion.SetResult();
        await save.ConfigureAwait(true);

        Assert.Equal("new.md", session.Destination);
        Assert.Equal(DocumentFormat.Markdown, session.Format);
        Assert.Equal("newer edit", session.Document.Text);
        Assert.Equal(identity, session.Document.Id);
        Assert.True(session.Document.IsDirty);
        session.Document.ReplaceText("loaded");
        Assert.True(session.Document.IsDirty);
        session.Document.ReplaceText("snapshot");
        Assert.False(session.Document.IsDirty);
        store.Save = _ => Task.CompletedTask;
        await session.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, store.Writes.Count);
    }

    /// <summary>Checks failure propagation and unchanged state for failed save and save-as.</summary>
    /// <param name="saveAs">Whether the operation changes the destination.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveFailurePreservesBaselineAndDestination(bool saveAs)
    {
        InvalidOperationException failure = new("Persistence failed.");
        MemoryStore store = new();
        DocumentSession session = await DocumentSession.LoadAsync(store, "old.txt", TestContext.Current.CancellationToken);
        Guid identity = session.Document.Id;
        session.Document.ReplaceText("draft");
        store.Save = _ => Task.FromException(failure);

        Exception actual = await Assert.ThrowsAsync<InvalidOperationException>(() => saveAs
            ? session.SaveAsAsync("new.md", TestContext.Current.CancellationToken)
            : session.SaveAsync(TestContext.Current.CancellationToken));

        Assert.Same(failure, actual);
        Assert.Equal("old.txt", session.Destination);
        Assert.Equal(DocumentFormat.PlainText, session.Format);
        Assert.Equal(identity, session.Document.Id);
        Assert.Equal("draft", session.Document.Text);
        Assert.True(session.Document.IsDirty);
        session.Document.ReplaceText("loaded");
        Assert.False(session.Document.IsDirty);
        store.Save = _ => Task.CompletedTask;
        await session.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(("old.txt", "loaded"), store.Writes[1]);
    }

    /// <summary>Checks cancellation during a pending save and retry after cancellation.</summary>
    [Fact]
    public async Task CancelledSaveCannotAcknowledgeOrChangeDestination()
    {
        using CancellationTokenSource cancellation = new();
        MemoryStore store = new() { Save = token => Task.Delay(Timeout.Infinite, token) };
        DocumentSession session = DocumentSession.CreateUntitled(store);
        session.Document.ReplaceText("draft");
        Task save = session.SaveAsAsync("cancelled.md", cancellation.Token);
        Assert.False(save.IsCompleted);
        await cancellation.CancelAsync();

        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Null(session.Destination);
        Assert.Equal(DocumentFormat.PlainText, session.Format);
        Assert.Equal("draft", session.Document.Text);
        Assert.True(session.Document.IsDirty);
        session.Document.ReplaceText("");
        Assert.False(session.Document.IsDirty);
        store.Save = _ => Task.CompletedTask;
        await session.SaveAsAsync("retry.txt", TestContext.Current.CancellationToken);
        Assert.Equal("retry.txt", session.Destination);
    }

    /// <summary>Checks that cancellation before an operation prevents port calls.</summary>
    [Fact]
    public async Task PreCancelledOperationsDoNotCallTheStore()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        MemoryStore store = new();
        DocumentSession session = DocumentSession.CreateUntitled(store);
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DocumentSession.LoadAsync(store, "file.txt", cancellation.Token));
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.SaveAsAsync("file.txt", cancellation.Token));
        Assert.Null(store.LoadDestination);
        Assert.Empty(store.Writes);
        Assert.Null(session.Destination);
        Assert.Equal(DocumentFormat.PlainText, session.Format);
    }

    /// <summary>Checks that an adapter cannot force acknowledgement after cancellation.</summary>
    [Fact]
    public async Task CancellationPreventsAcknowledgementWhenTheAdapterReturnsSuccess()
    {
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource completion = new();
        MemoryStore store = new() { Save = _ => completion.Task };
        DocumentSession session = await DocumentSession.LoadAsync(store, "old.txt", TestContext.Current.CancellationToken);
        session.Document.ReplaceText("snapshot");
        Task save = session.SaveAsAsync("new.md", cancellation.Token);
        session.Document.ReplaceText("newer edit");
        await cancellation.CancelAsync();
        completion.SetResult();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);

        Assert.Equal("old.txt", session.Destination);
        Assert.Equal(DocumentFormat.PlainText, session.Format);
        Assert.Equal("newer edit", session.Document.Text);
        Assert.True(session.Document.IsDirty);
        session.Document.ReplaceText("snapshot");
        Assert.True(session.Document.IsDirty);
        session.Document.ReplaceText("loaded");
        Assert.False(session.Document.IsDirty);
    }

    /// <summary>Checks that sessions do not block saves in other documents.</summary>
    [Fact]
    public async Task SeparateSessionsCanSaveWhileAnotherSaveIsPending()
    {
        TaskCompletionSource completion = new();
        MemoryStore store = new() { Save = _ => completion.Task };
        DocumentSession first = DocumentSession.CreateUntitled(store);
        DocumentSession second = DocumentSession.CreateUntitled(store);
        first.Document.ReplaceText("first");
        second.Document.ReplaceText("second");

        Task firstSave = first.SaveAsAsync("first.txt", TestContext.Current.CancellationToken);
        store.Save = _ => Task.CompletedTask;
        await second.SaveAsAsync("second.txt", TestContext.Current.CancellationToken);
        Assert.False(firstSave.IsCompleted);
        Assert.True(first.Document.IsDirty);
        Assert.False(second.Document.IsDirty);
        Assert.Equal(new[] { ("first.txt", "first"), ("second.txt", "second") }, store.Writes);
        completion.SetResult();
        await firstSave.ConfigureAwait(true);
        Assert.False(first.Document.IsDirty);
    }

    /// <summary>Checks required persistence dependencies at the application boundary.</summary>
    [Fact]
    public async Task NullStoreIsRejected()
    {
        _ = Assert.Throws<ArgumentNullException>(() => DocumentSession.CreateUntitled(null!));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => DocumentSession.LoadAsync(null!, "file.txt", TestContext.Current.CancellationToken));
    }

    /// <summary>Checks that load errors propagate without substitution.</summary>
    [Fact]
    public async Task LoadFailurePropagates()
    {
        InvalidOperationException failure = new("Load failed.");
        MemoryStore store = new() { Load = _ => Task.FromException<string>(failure) };
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DocumentSession.LoadAsync(store, "file.txt", TestContext.Current.CancellationToken)));
    }

    /// <summary>Checks cancellation during a pending load.</summary>
    [Fact]
    public async Task LoadCancellationPropagates()
    {
        using CancellationTokenSource cancellation = new();
        MemoryStore store = new()
        {
            Load = async token =>
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(true);
                return "unreachable";
            },
        };
        Task<DocumentSession> load = DocumentSession.LoadAsync(store, "file.txt", cancellation.Token);
        Assert.False(load.IsCompleted);
        await cancellation.CancelAsync();
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    /// <summary>Checks destination validation before persistence.</summary>
    /// <param name="destination">An invalid destination.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task InvalidDestinationsDoNotCallTheStore(string? destination)
    {
        MemoryStore store = new();
        DocumentSession session = DocumentSession.CreateUntitled(store);
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(() => DocumentSession.LoadAsync(store, destination!, TestContext.Current.CancellationToken));
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(() => session.SaveAsAsync(destination!, TestContext.Current.CancellationToken));
        Assert.Null(session.Destination);
        Assert.Equal(DocumentFormat.PlainText, session.Format);
        Assert.Null(store.LoadDestination);
        Assert.Empty(store.Writes);
    }

    /// <summary>Checks format transitions without changes to document identity or exact text.</summary>
    /// <param name="original">The loaded destination.</param>
    /// <param name="renamed">The saved destination.</param>
    /// <param name="initialFormat">The format before save-as.</param>
    /// <param name="savedFormat">The format after save-as.</param>
    [Theory]
    [InlineData("old.txt", "new.MD", DocumentFormat.PlainText, DocumentFormat.Markdown)]
    [InlineData("old.md", "new.txt", DocumentFormat.Markdown, DocumentFormat.PlainText)]
    [InlineData("old.markdown", "new.unknown", DocumentFormat.Markdown, DocumentFormat.PlainText)]
    [InlineData("old.unknown", "new.MARKDOWN", DocumentFormat.PlainText, DocumentFormat.Markdown)]
    [InlineData("old.md", "new", DocumentFormat.Markdown, DocumentFormat.PlainText)]
    public async Task SaveAsUpdatesFormatAfterSuccess(string original, string renamed,
        DocumentFormat initialFormat, DocumentFormat savedFormat)
    {
        const string text = "# Příliš 🐈\r\n漢字\n\0e\u0301";
        MemoryStore store = new() { Text = text };
        DocumentSession session = await DocumentSession.LoadAsync(store, original, TestContext.Current.CancellationToken);
        Guid identity = session.Document.Id;
        Assert.Equal(initialFormat, session.Format);
        Assert.False(session.Document.IsDirty);

        await session.SaveAsAsync(renamed, TestContext.Current.CancellationToken);

        Assert.Equal(savedFormat, session.Format);
        Assert.Equal(renamed, session.Destination);
        Assert.Equal(identity, session.Document.Id);
        Assert.Equal(text, session.Document.Text);
        Assert.False(session.Document.IsDirty);
        Assert.Equal((renamed, text), Assert.Single(store.Writes));
        await session.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(savedFormat, session.Format);
        Assert.Equal((renamed, text), store.Writes[1]);
    }

    /// <summary>Checks that the first successful save identifies an untitled document.</summary>
    [Fact]
    public async Task UntitledFormatChangesOnlyAfterSave()
    {
        MemoryStore store = new();
        DocumentSession session = DocumentSession.CreateUntitled(store);
        Guid identity = session.Document.Id;
        Assert.Equal(DocumentFormat.PlainText, session.Format);
        session.Document.ReplaceText("# draft");
        Assert.Equal(DocumentFormat.PlainText, session.Format);
        Assert.True(session.Document.IsDirty);

        await session.SaveAsAsync("first.md", TestContext.Current.CancellationToken);

        Assert.Equal(DocumentFormat.Markdown, session.Format);
        Assert.Equal(identity, session.Document.Id);
        Assert.Equal("# draft", session.Document.Text);
        Assert.False(session.Document.IsDirty);
    }

    private sealed class MemoryStore : ITextDocumentStore
    {
        public string Text { get; init; } = "loaded";
        public Func<CancellationToken, Task<string>>? Load { get; init; }
        public Func<CancellationToken, Task> Save { get; set; } = _ => Task.CompletedTask;
        public List<(string Destination, string Text)> Writes { get; } = [];
        public string? LoadDestination { get; private set; }
        public CancellationToken LoadToken { get; private set; }
        public CancellationToken SaveToken { get; private set; }

        public Task<string> LoadAsync(string destination, CancellationToken cancellationToken)
        {
            LoadDestination = destination;
            LoadToken = cancellationToken;
            return Load?.Invoke(cancellationToken) ?? Task.FromResult(Text);
        }

        public Task SaveAsync(string destination, string text, CancellationToken cancellationToken)
        {
            Writes.Add((destination, text));
            SaveToken = cancellationToken;
            return Save(cancellationToken);
        }
    }
}

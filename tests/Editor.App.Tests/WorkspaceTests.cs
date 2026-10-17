using Editor.Application;
using Xunit;

namespace Editor.App.Tests;

/// <summary>Checks presentation behavior through the editor commands.</summary>
public sealed class WorkspaceTests
{
    /// <summary>A pending save must not acknowledge later edits or permit replacement.</summary>
    [Fact]
    public async Task SaveBeforeNewKeepsEditsMadeDuringSave()
    {
        Store store = new();
        Dialogs dialogs = new() { Decision = UnsavedDecision.Save, SavePath = "/saved.txt" };
        EditorWorkspace workspace = new(store, dialogs);
        workspace.Edit("first\r\n");
        Task<bool> replacing = workspace.NewAsync();
        Assert.Equal("first\r\n", store.Snapshot);
        workspace.Edit("newer\n");
        store.PendingSave.SetResult();
        Assert.False(await replacing.ConfigureAwait(true));
        Assert.Equal("newer\n", workspace.Session.Document.Text);
        Assert.True(workspace.Session.Document.IsDirty);
        Assert.Equal("/saved.txt", workspace.Session.Destination);
        Assert.False(workspace.IsBusy);
    }

    /// <summary>Cancelled pickers and failed loads preserve the current document.</summary>
    [Fact]
    public async Task OpenCancellationAndFailurePreserveDocument()
    {
        Store store = new();
        Dialogs dialogs = new() { Decision = UnsavedDecision.Discard };
        EditorWorkspace workspace = new(store, dialogs);
        workspace.Edit("keep");
        Guid id = workspace.Session.Document.Id;
        Assert.False(await workspace.OpenAsync().ConfigureAwait(true));
        dialogs.OpenPath = "/missing.txt";
        store.LoadError = new IOException("missing");
        Assert.False(await workspace.OpenAsync().ConfigureAwait(true));
        Assert.Equal(id, workspace.Session.Document.Id);
        Assert.Equal("keep", workspace.Session.Document.Text);
        Assert.True(workspace.Session.Document.IsDirty);
        Assert.Equal("missing", dialogs.Error);
    }

    /// <summary>A cancelled save and a cancelled confirmation must prevent closing.</summary>
    [Fact]
    public async Task CancelledSaveAndConfirmationPreventClose()
    {
        Store store = new();
        Dialogs dialogs = new();
        EditorWorkspace workspace = new(store, dialogs);
        workspace.Edit("keep");
        Assert.False(await workspace.CanCloseAsync().ConfigureAwait(true));
        dialogs.Decision = UnsavedDecision.Save;
        Assert.False(await workspace.CanCloseAsync().ConfigureAwait(true));
        dialogs.SavePath = "/save.txt";
        Task<bool> saving = workspace.SaveAsync();
        workspace.Cancel();
        store.PendingSave.SetCanceled(TestContext.Current.CancellationToken);
        Assert.False(await saving.ConfigureAwait(true));
        Assert.Null(workspace.Session.Destination);
        Assert.True(workspace.Session.Document.IsDirty);
        Assert.Null(dialogs.Error);
    }

    /// <summary>Busy commands cannot replace a document while its save is pending.</summary>
    [Fact]
    public async Task PendingSaveRejectsOtherCommandsAndFailureKeepsBaseline()
    {
        Store store = new();
        Dialogs dialogs = new() { SavePath = "/save.txt" };
        EditorWorkspace workspace = new(store, dialogs);
        workspace.Edit("keep");
        Task<bool> saving = workspace.SaveAsync();
        Assert.True(workspace.IsBusy);
        Assert.True(workspace.CanEdit);
        Assert.False(await workspace.NewAsync().ConfigureAwait(true));
        Assert.False(await workspace.OpenAsync().ConfigureAwait(true));
        Assert.False(await workspace.SaveAsync().ConfigureAwait(true));
        Assert.False(await workspace.CanCloseAsync().ConfigureAwait(true));
        store.PendingSave.SetException(new UnauthorizedAccessException("denied"));
        Assert.False(await saving.ConfigureAwait(true));
        Assert.Equal("denied", dialogs.Error);
        Assert.Null(workspace.Session.Destination);
        Assert.True(workspace.Session.Document.IsDirty);
        Assert.False(workspace.IsBusy);
    }

    /// <summary>A cancelled pending open preserves the old identity and contents.</summary>
    [Fact]
    public async Task CancelPendingOpenPreservesDocument()
    {
        Store store = new() { PendingLoad = new TaskCompletionSource<string>() };
        Dialogs dialogs = new() { OpenPath = "/input.txt", Decision = UnsavedDecision.Discard };
        EditorWorkspace workspace = new(store, dialogs);
        workspace.Edit("keep");
        Guid identity = workspace.Session.Document.Id;
        Task<bool> opening = workspace.OpenAsync();
        Assert.False(workspace.CanEdit);
        workspace.Cancel();
        store.PendingLoad.SetResult("must not replace");
        Assert.False(await opening.ConfigureAwait(true));
        Assert.Equal("keep", workspace.Session.Document.Text);
        Assert.Equal(identity, workspace.Session.Document.Id);
        Assert.True(workspace.Session.Document.IsDirty);
        Assert.True(workspace.CanEdit);
    }

    /// <summary>Saving before close permits close only after the captured text is saved.</summary>
    [Fact]
    public async Task SaveBeforeCloseAndSubsequentSaveUseSuccessfulDestination()
    {
        Store store = new();
        Dialogs dialogs = new() { SavePath = "/save.txt", Decision = UnsavedDecision.Save };
        EditorWorkspace workspace = new(store, dialogs);
        workspace.Edit("saved");
        Task<bool> closing = workspace.CanCloseAsync();
        Assert.True(workspace.IsBusy);
        store.PendingSave.SetResult();
        Assert.True(await closing.ConfigureAwait(true));
        Assert.False(workspace.Session.Document.IsDirty);
        dialogs.SavePath = null;
        workspace.Edit("again");
        Assert.True(await workspace.SaveAsync().ConfigureAwait(true));
        Assert.Equal("again", store.Snapshot);
        Assert.Equal("/save.txt", workspace.Session.Destination);
        Assert.False(workspace.Session.Document.IsDirty);
    }

    private sealed class Store : ITextDocumentStore
    {
        internal TaskCompletionSource PendingSave { get; } = new();
        internal string? Snapshot { get; private set; }
        internal Exception? LoadError { get; set; }
        internal TaskCompletionSource<string>? PendingLoad { get; init; }

        public Task<string> LoadAsync(string destination, CancellationToken cancellationToken = default)
        {
            return PendingLoad?.Task ?? (LoadError is { } error ? Task.FromException<string>(error) : Task.FromResult("loaded\r\n\n"));
        }

        public Task SaveAsync(string destination, string text, CancellationToken cancellationToken = default)
        {
            Snapshot = text;
            return PendingSave.Task;
        }
    }

    private sealed class Dialogs : IEditorDialogs
    {
        internal UnsavedDecision Decision { get; set; } = UnsavedDecision.Cancel;
        internal string? OpenPath { get; set; }
        internal string? SavePath { get; set; }
        internal string? Error { get; private set; }
        public Task<string?> ChooseOpenAsync()
        {
            return Task.FromResult(OpenPath);
        }

        public Task<string?> ChooseSaveAsync(string? destination)
        {
            return Task.FromResult(SavePath);
        }

        public Task<UnsavedDecision> ConfirmUnsavedAsync()
        {
            return Task.FromResult(Decision);
        }

        public Task ShowErrorAsync(string message) { Error = message; return Task.CompletedTask; }
    }
}

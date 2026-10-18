using Editor.App.Resources;
using Editor.App.Services;
using Editor.App.ViewModels;
using Editor.Application;
using Xunit;

namespace Editor.App.Tests.ViewModels;

/// <summary>Checks presentation behavior through the editor commands.</summary>
public sealed class EditorViewModelTests
{
    /// <summary>Generated commands notify consumers and reject overlapping document operations.</summary>
    [Fact]
    public async Task CommandsPublishBusyStateAndPreservePendingEdits()
    {
        Store store = new();
        Dialogs dialogs = new() { SavePath = "/save.txt" };
        EditorViewModel viewModel = new(store, dialogs, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"));
        List<string?> notifications = [];
        List<bool> availability = [];
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        viewModel.NewCommand.CanExecuteChanged += (_, _) => availability.Add(viewModel.NewCommand.CanExecute(null));
        viewModel.Text = "first";
        Assert.Equal("Untitled * - Tangopad", viewModel.Title);
        Task saving = viewModel.SaveCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsBusy);
        Assert.True(viewModel.CanEdit);
        Assert.False(viewModel.NewCommand.CanExecute(null));
        Assert.False(viewModel.OpenCommand.CanExecute(null));
        Assert.False(viewModel.SaveCommand.CanExecute(null));
        Assert.False(viewModel.SaveAsCommand.CanExecute(null));
        Assert.True(viewModel.CancelCommand.CanExecute(null));
        await viewModel.OpenCommand.ExecuteAsync(null).ConfigureAwait(true);
        viewModel.Text = "later";
        store.PendingSave.SetResult();
        await saving.ConfigureAwait(true);
        Assert.Equal("first", store.Snapshot);
        Assert.Equal("later", viewModel.Text);
        Assert.Equal("/save.txt * - Tangopad", viewModel.Title);
        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.NewCommand.CanExecute(null));
        Assert.False(viewModel.CancelCommand.CanExecute(null));
        Assert.Equal([false, true], availability);
        Assert.Contains("Title", notifications);
        Assert.Contains("IsDirty", notifications);
        Assert.Contains("IsBusy", notifications);
    }

    /// <summary>The generated cancellation command preserves unsaved text when a store ignores cancellation.</summary>
    [Fact]
    public async Task CancelCommandPreservesTheDirtyBaseline()
    {
        Store store = new();
        EditorViewModel viewModel = new(store, new Dialogs { SavePath = "/save.txt" },
            new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"))
        {
            Text = "keep"
        };
        Task saving = viewModel.SaveCommand.ExecuteAsync(null);
        viewModel.CancelCommand.Execute(null);
        store.PendingSave.SetResult();
        await saving.ConfigureAwait(true);
        Assert.Equal("keep", viewModel.Text);
        Assert.True(viewModel.IsDirty);
        Assert.Null(viewModel.Session.Destination);
        Assert.False(viewModel.IsBusy);
    }

    /// <summary>A pending save must not acknowledge later edits or permit replacement.</summary>
    [Fact]
    public async Task SaveBeforeNewKeepsEditsMadeDuringSave()
    {
        Store store = new();
        Dialogs dialogs = new() { Decision = UnsavedDecision.Save, SavePath = "/saved.txt" };
        EditorViewModel viewModel = new(store, dialogs, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"))
        {
            Text = "first\r\n"
        };
        Task<bool> replacing = viewModel.NewAsync();
        Assert.Equal("first\r\n", store.Snapshot);
        viewModel.Text = "newer\n";
        store.PendingSave.SetResult();
        Assert.False(await replacing.ConfigureAwait(true));
        Assert.Equal("newer\n", viewModel.Session.Document.Text);
        Assert.True(viewModel.Session.Document.IsDirty);
        Assert.Equal("/saved.txt", viewModel.Session.Destination);
        Assert.False(viewModel.IsBusy);
    }

    /// <summary>Cancelled pickers and failed loads preserve the current document.</summary>
    [Fact]
    public async Task OpenCancellationAndFailurePreserveDocument()
    {
        Store store = new();
        Dialogs dialogs = new() { Decision = UnsavedDecision.Discard };
        EditorViewModel viewModel = new(store, dialogs, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"))
        {
            Text = "keep"
        };
        Guid id = viewModel.Session.Document.Id;
        Assert.False(await viewModel.OpenAsync().ConfigureAwait(true));
        dialogs.OpenPath = "/missing.txt";
        store.LoadError = new IOException("missing");
        Assert.False(await viewModel.OpenAsync().ConfigureAwait(true));
        Assert.Equal(id, viewModel.Session.Document.Id);
        Assert.Equal("keep", viewModel.Session.Document.Text);
        Assert.True(viewModel.Session.Document.IsDirty);
        Assert.Equal("missing", dialogs.Error);
    }

    /// <summary>A cancelled save and a cancelled confirmation must prevent closing.</summary>
    [Fact]
    public async Task CancelledSaveAndConfirmationPreventClose()
    {
        Store store = new();
        Dialogs dialogs = new();
        EditorViewModel viewModel = new(store, dialogs, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"))
        {
            Text = "keep"
        };
        Assert.False(await viewModel.CanCloseAsync().ConfigureAwait(true));
        dialogs.Decision = UnsavedDecision.Save;
        Assert.False(await viewModel.CanCloseAsync().ConfigureAwait(true));
        dialogs.SavePath = "/save.txt";
        Task<bool> saving = viewModel.SaveAsync();
        viewModel.Cancel();
        store.PendingSave.SetCanceled(TestContext.Current.CancellationToken);
        Assert.False(await saving.ConfigureAwait(true));
        Assert.Null(viewModel.Session.Destination);
        Assert.True(viewModel.Session.Document.IsDirty);
        Assert.Null(dialogs.Error);
    }

    /// <summary>Busy commands cannot replace a document while its save is pending.</summary>
    [Fact]
    public async Task PendingSaveRejectsOtherCommandsAndFailureKeepsBaseline()
    {
        Store store = new();
        Dialogs dialogs = new() { SavePath = "/save.txt" };
        EditorViewModel viewModel = new(store, dialogs, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"))
        {
            Text = "keep"
        };
        Task<bool> saving = viewModel.SaveAsync();
        Assert.True(viewModel.IsBusy);
        Assert.True(viewModel.CanEdit);
        Assert.False(await viewModel.NewAsync().ConfigureAwait(true));
        Assert.False(await viewModel.OpenAsync().ConfigureAwait(true));
        Assert.False(await viewModel.SaveAsync().ConfigureAwait(true));
        Assert.False(await viewModel.CanCloseAsync().ConfigureAwait(true));
        store.PendingSave.SetException(new UnauthorizedAccessException("denied"));
        Assert.False(await saving.ConfigureAwait(true));
        Assert.Equal("denied", dialogs.Error);
        Assert.Null(viewModel.Session.Destination);
        Assert.True(viewModel.Session.Document.IsDirty);
        Assert.False(viewModel.IsBusy);
    }

    /// <summary>A cancelled pending open preserves the old identity and contents.</summary>
    [Fact]
    public async Task CancelPendingOpenPreservesDocument()
    {
        Store store = new() { PendingLoad = new TaskCompletionSource<string>() };
        Dialogs dialogs = new() { OpenPath = "/input.txt", Decision = UnsavedDecision.Discard };
        EditorViewModel viewModel = new(store, dialogs, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"))
        {
            Text = "keep"
        };
        Guid identity = viewModel.Session.Document.Id;
        Task<bool> opening = viewModel.OpenAsync();
        Assert.False(viewModel.CanEdit);
        viewModel.Cancel();
        store.PendingLoad.SetResult("must not replace");
        Assert.False(await opening.ConfigureAwait(true));
        Assert.Equal("keep", viewModel.Session.Document.Text);
        Assert.Equal(identity, viewModel.Session.Document.Id);
        Assert.True(viewModel.Session.Document.IsDirty);
        Assert.True(viewModel.CanEdit);
    }

    /// <summary>Saving before close permits close only after the captured text is saved.</summary>
    [Fact]
    public async Task SaveBeforeCloseAndSubsequentSaveUseSuccessfulDestination()
    {
        Store store = new();
        Dialogs dialogs = new() { SavePath = "/save.txt", Decision = UnsavedDecision.Save };
        EditorViewModel viewModel = new(store, dialogs, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"))
        {
            Text = "saved"
        };
        Task<bool> closing = viewModel.CanCloseAsync();
        Assert.True(viewModel.IsBusy);
        store.PendingSave.SetResult();
        Assert.True(await closing.ConfigureAwait(true));
        Assert.False(viewModel.Session.Document.IsDirty);
        dialogs.SavePath = null;
        viewModel.Text = "again";
        Assert.True(await viewModel.SaveAsync().ConfigureAwait(true));
        Assert.Equal("again", store.Snapshot);
        Assert.Equal("/save.txt", viewModel.Session.Destination);
        Assert.False(viewModel.Session.Document.IsDirty);
    }

    /// <summary>Word Wrap is observable presentation state that survives document replacement.</summary>
    [Fact]
    public async Task WordWrapPreservesDocumentStateAcrossNewAndOpen()
    {
        EditorViewModel viewModel = new(new Store(), new Dialogs
        {
            Decision = UnsavedDecision.Discard,
            OpenPath = "/input.md"
        }, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"));
        Assert.False(viewModel.WordWrap);
        Guid identity = viewModel.DocumentId;
        List<string?> notifications = [];
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        viewModel.WordWrap = true;
        Assert.Equal(["WordWrap"], notifications);
        Assert.Equal(identity, viewModel.DocumentId);
        Assert.Equal(string.Empty, viewModel.Text);
        Assert.False(viewModel.IsDirty);
        viewModel.Text = "λ😀\r\nline\nlast\r";
        notifications.Clear();
        viewModel.WordWrap = false;
        Assert.Equal(["WordWrap"], notifications);
        Assert.Equal(identity, viewModel.DocumentId);
        Assert.Equal("λ😀\r\nline\nlast\r", viewModel.Session.Document.Text);
        Assert.True(viewModel.IsDirty);
        viewModel.WordWrap = true;
        Assert.True(await viewModel.NewAsync().ConfigureAwait(true));
        Assert.True(viewModel.WordWrap);
        Assert.Equal(string.Empty, viewModel.Text);
        Assert.False(viewModel.IsDirty);
        Assert.True(await viewModel.OpenAsync().ConfigureAwait(true));
        Assert.True(viewModel.WordWrap);
        Assert.Equal("loaded\r\n\n", viewModel.Text);
        Assert.False(viewModel.IsDirty);
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

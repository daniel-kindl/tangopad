using Editor.App.Resources;
using Editor.App.Services;
using Editor.App.ViewModels;
using Editor.Application;
using Editor.Core;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests.ViewModels;

/// <summary>Checks explicit reload through the current destination and replacement guard.</summary>
public sealed class ReloadTests
{
    /// <summary>Clean reload creates a clean identity with exact external text and refreshed counts.</summary>
    /// <param name="path">The saved destination.</param>
    /// <param name="format">The expected format.</param>
    [Theory]
    [InlineData("/current.md", DocumentFormat.Markdown)]
    [InlineData("/current.txt", DocumentFormat.PlainText)]
    public async Task CleanReloadReplacesIdentityAndRecordsHistory(string path, DocumentFormat format)
    {
        Store store = new();
        RecentFileHistory history = new();
        EditorViewModel model = Create(store, new(), history);
        Assert.True(await model.OpenPathAsync(path).ConfigureAwait(true));
        history.Record("/other.txt");
        Guid identity = model.DocumentId;
        store.Text = "😀\r\nλ\nlast\r";
        Assert.True(await model.ReloadAsync().ConfigureAwait(true));
        Assert.NotEqual(identity, model.DocumentId);
        Assert.Equal("😀\r\nλ\nlast\r", model.Text);
        Assert.Equal("😀\r\nλ\nlast\r", model.Session.Document.Text);
        Assert.False(model.IsDirty);
        Assert.Equal(path, store.LoadedPath);
        Assert.Equal(path, model.Session.Destination);
        Assert.Equal(format, model.Format);
        Assert.Equal(10, model.CharacterCount);
        Assert.Equal(3, model.WordCount);
        Assert.Equal(LineEndingStyle.Mixed, model.LineEndings);
        Assert.Equal([path, "/other.txt"], model.RecentPaths);
        Assert.True(model.ReloadCommand.CanExecute(null));
    }

    /// <summary>Dirty reload uses the established Save, Discard, and Cancel decisions.</summary>
    /// <param name="choice">The confirmation result.</param>
    [Theory]
    [InlineData("Save")]
    [InlineData("Discard")]
    [InlineData("Cancel")]
    public async Task DirtyReloadHonorsConfirmation(string choice)
    {
        UnsavedDecision decision = Enum.Parse<UnsavedDecision>(choice);
        Store store = new();
        Dialogs dialogs = new() { Decision = decision };
        EditorViewModel model = Create(store, dialogs);
        Assert.True(await model.OpenPathAsync("/current.md").ConfigureAwait(true));
        Guid identity = model.DocumentId;
        model.Text = "unsaved";
        store.Text = "external";
        bool replaced = await model.ReloadAsync().ConfigureAwait(true);
        Assert.Equal(decision != UnsavedDecision.Cancel, replaced);
        Assert.Equal(1, dialogs.Confirmations);
        Assert.Equal(decision == UnsavedDecision.Cancel ? 1 : 2, store.Loads);
        Assert.Equal(decision == UnsavedDecision.Save ? "unsaved" : null, store.SavedText);
        Assert.Equal(decision switch
        {
            UnsavedDecision.Save => "unsaved",
            UnsavedDecision.Discard => "external",
            UnsavedDecision.Cancel => "unsaved",
            _ => throw new InvalidOperationException("Unknown confirmation result.")
        }, model.Text);
        Assert.Equal(decision == UnsavedDecision.Cancel, model.IsDirty);
        Assert.Equal(decision == UnsavedDecision.Cancel, identity == model.DocumentId);
    }

    /// <summary>Failed loads keep the session, with normal save side effects when Save succeeded first.</summary>
    /// <param name="save">Whether to save before the failed load.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailurePreservesSessionAndSuccessfulSave(bool save)
    {
        Store store = new();
        Dialogs dialogs = new() { Decision = save ? UnsavedDecision.Save : UnsavedDecision.Discard };
        EditorViewModel model = Create(store, dialogs);
        Assert.True(await model.OpenPathAsync("/current.txt").ConfigureAwait(true));
        DocumentSession session = model.Session;
        model.Text = "keep";
        store.LoadError = new UnauthorizedAccessException("Access denied.");
        Assert.False(await model.ReloadAsync().ConfigureAwait(true));
        Assert.Same(session, model.Session);
        Assert.Equal("keep", model.Text);
        Assert.Equal(!save, model.IsDirty);
        Assert.Equal("/current.txt", model.Session.Destination);
        Assert.Equal("Access denied.", dialogs.Error);
        Assert.Equal(save ? "keep" : null, store.SavedText);
        Assert.Equal(["/current.txt"], model.RecentPaths);
    }

    /// <summary>Cancellation and reentry cannot replace the active session, even if storage ignores cancellation.</summary>
    [Fact]
    public async Task PendingReloadRejectsReentryAndCancellationPreservesSession()
    {
        Store store = new();
        EditorViewModel model = Create(store, new() { Decision = UnsavedDecision.Discard });
        Assert.False(model.ReloadCommand.CanExecute(null));
        Assert.False(await model.ReloadAsync().ConfigureAwait(true));
        Assert.Equal(0, store.Loads);
        Assert.True(await model.OpenPathAsync("/current.txt").ConfigureAwait(true));
        model.Text = "keep";
        DocumentSession session = model.Session;
        store.PendingLoad = new();
        Task<bool> pending = model.ReloadAsync();
        Assert.True(model.IsBusy);
        Assert.False(model.CanEdit);
        Assert.False(model.ReloadCommand.CanExecute(null));
        Assert.False(await model.ReloadAsync().ConfigureAwait(true));
        Assert.False(await model.NewAsync().ConfigureAwait(true));
        Assert.False(await model.OpenPathAsync("/other.txt").ConfigureAwait(true));
        Assert.False(await model.SaveAsync().ConfigureAwait(true));
        model.Cancel();
        store.PendingLoad.SetResult("must not replace");
        Assert.False(await pending.ConfigureAwait(true));
        Assert.Same(session, model.Session);
        Assert.Equal("keep", model.Text);
        Assert.True(model.IsDirty);
        Assert.Equal(2, store.Loads);
        Assert.True(model.CanEdit);
        Assert.True(model.ReloadCommand.CanExecute(null));
    }

    /// <summary>New edits during confirmation Save prevent reload.</summary>
    [Fact]
    public async Task EditsDuringSavePreventReplacement()
    {
        Store store = new();
        EditorViewModel model = Create(store, new() { Decision = UnsavedDecision.Save });
        Assert.True(await model.OpenPathAsync("/current.txt").ConfigureAwait(true));
        Guid identity = model.DocumentId;
        model.Text = "snapshot";
        store.PendingSave = new();
        Task<bool> pending = model.ReloadAsync();
        Assert.True(model.CanEdit);
        model.Text = "later edit";
        store.PendingSave.SetResult();
        Assert.False(await pending.ConfigureAwait(true));
        Assert.Equal("snapshot", store.SavedText);
        Assert.Equal("later edit", model.Text);
        Assert.True(model.IsDirty);
        Assert.Equal(identity, model.DocumentId);
        Assert.Equal(1, store.Loads);
    }

    private static EditorViewModel Create(Store store, Dialogs dialogs, RecentFileHistory? history = null)
    {
        return new(store, dialogs, new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"), history);
    }

    private sealed class Store : ITextDocumentStore
    {
        internal string Text { get; set; } = "original";
        internal string? LoadedPath { get; private set; }
        internal string? SavedText { get; private set; }
        internal int Loads { get; private set; }
        internal Exception? LoadError { get; set; }
        internal TaskCompletionSource<string>? PendingLoad { get; set; }
        internal TaskCompletionSource? PendingSave { get; set; }

        public Task<string> LoadAsync(string destination, CancellationToken cancellationToken = default)
        {
            Loads++;
            LoadedPath = destination;
            return PendingLoad?.Task ?? (LoadError is { } error ? Task.FromException<string>(error) : Task.FromResult(Text));
        }

        public async Task SaveAsync(string destination, string text, CancellationToken cancellationToken = default)
        {
            SavedText = text;
            if (PendingSave is { } pending)
            {
                await pending.Task.ConfigureAwait(true);
            }

            Text = text;
        }
    }

    private sealed class Dialogs : IEditorDialogs
    {
        internal UnsavedDecision Decision { get; init; }
        internal int Confirmations { get; private set; }
        internal string? Error { get; private set; }
        public Task<string?> ChooseOpenAsync()
        {
            throw new InvalidOperationException("Reload must not open a picker.");
        }

        public Task<string?> ChooseSaveAsync(string? destination)
        {
            throw new InvalidOperationException("The document already has a destination.");
        }

        public Task<UnsavedDecision> ConfirmUnsavedAsync()
        {
            Confirmations++;
            return Task.FromResult(Decision);
        }

        public Task ShowErrorAsync(string message) { Error = message; return Task.CompletedTask; }
    }
}

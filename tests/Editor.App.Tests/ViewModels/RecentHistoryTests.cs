using Editor.App.Resources;
using Editor.App.Services;
using Editor.App.ViewModels;
using Editor.Application;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests.ViewModels;

/// <summary>Checks history isolation from document failures and settings failures.</summary>
public sealed class RecentHistoryTests
{
    /// <summary>Denied file access preserves history, dirty text, and identity for Open and Save As.</summary>
    [Fact]
    public async Task FileFailuresDoNotRecordDestinations()
    {
        string old = Path.GetFullPath("old.txt");
        RecentFileHistory history = new();
        history.Record(old);
        Dialogs dialogs = new();
        EditorViewModel viewModel = new(new DeniedStore(), dialogs,
            new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"), history)
        { Text = "keep" };
        Guid identity = viewModel.DocumentId;
        Assert.False(await viewModel.OpenPathAsync("denied.md").ConfigureAwait(true));
        Assert.Equal("Access denied.", dialogs.Error);
        Assert.Equal([old], viewModel.RecentPaths);
        Assert.Equal(identity, viewModel.DocumentId);
        Assert.Equal("keep", viewModel.Text);
        Assert.True(viewModel.IsDirty);
        Assert.Null(viewModel.Session.Destination);
        Assert.False(await viewModel.SaveAsAsync().ConfigureAwait(true));
        Assert.Equal([old], viewModel.RecentPaths);
        Assert.Equal(identity, viewModel.DocumentId);
        Assert.True(viewModel.IsDirty);
        Assert.Null(viewModel.Session.Destination);
    }

    /// <summary>A successful document save remains successful when history cannot be stored.</summary>
    [Fact]
    public async Task SettingsFailuresPreserveDocumentSaveAndClearWarns()
    {
        string directory = Directory.CreateTempSubdirectory("tangopad-recent-vm-").FullName;
        try
        {
            string blocker = Path.Combine(directory, "blocker");
            string document = Path.Combine(directory, "saved.txt");
            await File.WriteAllTextAsync(blocker, "block", TestContext.Current.CancellationToken).ConfigureAwait(true);
            RecentFileHistory history = new(Path.Combine(blocker, "recent-files.json"));
            EditorViewModel viewModel = new(new FileTextDocumentStore(), new Dialogs { SavePath = document },
                new EditorLabels("Untitled", "{0}{1} - Tangopad", " *"), history)
            { Text = "saved" };
            await viewModel.InitializeHistoryAsync().ConfigureAwait(true);
            Guid identity = viewModel.DocumentId;
            Assert.True(await viewModel.SaveAsync().ConfigureAwait(true));
            Assert.False(viewModel.IsDirty);
            Assert.Equal(document, viewModel.Session.Destination);
            Assert.Equal("saved", await File.ReadAllTextAsync(document, TestContext.Current.CancellationToken).ConfigureAwait(true));
            Assert.Equal([document], viewModel.RecentPaths);
            await viewModel.ClearRecentFilesCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.True(viewModel.HistoryStorageFailed);
            Assert.Empty(viewModel.RecentPaths);
            Assert.Equal(identity, viewModel.DocumentId);
            Assert.Equal("saved", viewModel.Text);
            Assert.False(viewModel.IsDirty);
            Assert.Equal(document, viewModel.Session.Destination);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class DeniedStore : ITextDocumentStore
    {
        public async Task<StoredDocument> LoadAsync(string destination, CancellationToken cancellationToken)
        {
            return new(await Task.FromException<string>(new UnauthorizedAccessException("Access denied.")).ConfigureAwait(true), destination);
        }

        public async Task<string?> SaveAsync(string destination, string text, string? expectedRevision, bool overwrite, CancellationToken cancellationToken)
        {
            await Task.FromException(new UnauthorizedAccessException("Access denied.")).ConfigureAwait(true);
            return destination;
        }
    }

    private sealed class Dialogs : IEditorDialogs
    {
        public Task<ConflictDecision> ConfirmConflictAsync(string destination) { return Task.FromResult(ConflictDecision.Cancel); }
        internal string? SavePath { get; init; } = "denied.txt";
        internal string? Error { get; private set; }
        public Task<string?> ChooseOpenAsync() { return Task.FromResult<string?>(null); }
        public Task<string?> ChooseSaveAsync(string? destination) { return Task.FromResult(SavePath); }
        public Task<UnsavedDecision> ConfirmUnsavedAsync() { return Task.FromResult(UnsavedDecision.Discard); }
        public Task ShowErrorAsync(string message) { Error = message; return Task.CompletedTask; }
    }
}

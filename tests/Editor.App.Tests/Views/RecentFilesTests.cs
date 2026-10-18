using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Editor.App.Services;
using Editor.App.Views;
using Editor.Core;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks recent-file controls through the real editor and file store.</summary>
public sealed class RecentFilesTests
{
    /// <summary>Reopening uses the shared dirty-document workflow and preserves or replaces the buffer correctly.</summary>
    /// <param name="decisionName">The unsaved-change decision.</param>
    [Theory]
    [InlineData("Cancel")]
    [InlineData("Discard")]
    [InlineData("Save")]
    public async Task RecentMenuUsesOpenWorkflow(string decisionName)
    {
        UnsavedDecision decision = Enum.Parse<UnsavedDecision>(decisionName);
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string directory = Directory.CreateTempSubdirectory("tangopad-recent-ui-").FullName;
            try
            {
                string path = Path.Combine(directory, "next.md");
                string saved = Path.Combine(directory, "saved.txt");
                await File.WriteAllTextAsync(path, "# λ\r\ntext").ConfigureAwait(true);
                RecentFileHistory history = new();
                history.Record(path);
                Dialogs dialogs = new() { Decision = decision, SavePath = saved };
                EditorWindow window = new(new FileTextDocumentStore(), dialogs, recentFiles: history);
                window.Show();
                window.Editor.Text = "original";
                window.Editor.Document.Insert(0, "!");
                window.Editor.Select(2, 3);
                Guid identity = window.ViewModel.DocumentId;
                object buffer = window.Editor.Document;
                int caret = window.Editor.CaretOffset;
                window.ViewModel.WordWrap = true;
                window.ViewModel.IncreaseFontSizeCommand.Execute(null);
                Button item = ShowRecent(window, path);
                Assert.True(item.IsEnabled);
                Assert.Equal(path, item.CommandParameter);
                Click(item);
                Task? opening = window.ViewModel.OpenRecentCommand.ExecutionTask;
                Assert.NotNull(opening);
                await opening.ConfigureAwait(true);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, dialogs.Confirmations);
                Assert.Equal(0, dialogs.Pickers);
                Assert.True(window.ViewModel.WordWrap);
                Assert.Equal(16, window.Editor.FontSize);
                if (decision == UnsavedDecision.Cancel)
                {
                    Assert.Equal(identity, window.ViewModel.DocumentId);
                    Assert.Same(buffer, window.Editor.Document);
                    Assert.Equal("!original", window.Editor.Text);
                    Assert.Equal(caret, window.Editor.CaretOffset);
                    Assert.Equal(3, window.Editor.SelectionLength);
                    Assert.True(window.Editor.Document.UndoStack.CanUndo);
                    Assert.True(window.ViewModel.IsDirty);
                    Assert.Null(window.ViewModel.Session.Destination);
                    Assert.Equal([path], window.ViewModel.RecentPaths);
                }
                else
                {
                    Assert.NotEqual(identity, window.ViewModel.DocumentId);
                    Assert.Equal("# λ\r\ntext", window.Editor.Text);
                    Assert.Equal(path, window.ViewModel.Session.Destination);
                    Assert.Equal(DocumentFormat.Markdown, window.ViewModel.Format);
                    Assert.NotNull(window.Editor.SyntaxHighlighting);
                    Assert.False(window.ViewModel.IsDirty);
                    Assert.False(window.Editor.Document.UndoStack.CanUndo);
                    Assert.Equal(path, window.ViewModel.RecentPaths[0]);
                    if (decision == UnsavedDecision.Save)
                    {
                        Assert.Equal("!original", await File.ReadAllTextAsync(saved).ConfigureAwait(true));
                        Assert.Equal([path, saved], window.ViewModel.RecentPaths);
                    }
                }

                window.ViewModel.ClearRecentFilesCommand.Execute(null);
                Assert.Empty(history.Paths);
                Assert.Equal(decision == UnsavedDecision.Cancel ? "!original" : "# λ\r\ntext", window.Editor.Text);
                dialogs.Decision = UnsavedDecision.Discard;
                window.Close();
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Missing, invalid UTF-8, and directory entries retain history and leave the active editor intact.</summary>
    /// <param name="failure">The failing file case.</param>
    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("directory")]
    public async Task FailedRecentOpenPreservesEditingState(string failure)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string directory = Directory.CreateTempSubdirectory("tangopad-recent-ui-").FullName;
            try
            {
                string path = failure == "directory" ? directory : Path.Combine(directory, "bad.md");
                if (failure == "invalid")
                {
                    await File.WriteAllBytesAsync(path, [0xff, 0xfe]).ConfigureAwait(true);
                }

                RecentFileHistory history = new();
                history.Record(path);
                Dialogs dialogs = new() { Decision = UnsavedDecision.Discard };
                EditorWindow window = new(new FileTextDocumentStore(), dialogs, recentFiles: history);
                window.Show();
                window.Editor.Text = "keep";
                window.Editor.Document.Insert(0, "!");
                window.Editor.Select(1, 2);
                Guid identity = window.ViewModel.DocumentId;
                object buffer = window.Editor.Document;
                Button item = ShowRecent(window, path);
                Click(item);
                Assert.NotNull(window.ViewModel.OpenRecentCommand.ExecutionTask);
                await window.ViewModel.OpenRecentCommand.ExecutionTask.ConfigureAwait(true);
                Assert.NotNull(dialogs.Error);
                Assert.Equal(identity, window.ViewModel.DocumentId);
                Assert.Same(buffer, window.Editor.Document);
                Assert.Equal("!keep", window.Editor.Text);
                Assert.Equal(2, window.Editor.SelectionLength);
                Assert.True(window.ViewModel.IsDirty);
                Assert.True(window.Editor.Document.UndoStack.CanUndo);
                Assert.Null(window.ViewModel.Session.Destination);
                Assert.Equal(DocumentFormat.PlainText, window.ViewModel.Format);
                Assert.Null(window.Editor.SyntaxHighlighting);
                Assert.Equal([path], history.Paths);
                Assert.True(window.ViewModel.CanEdit);
                window.Close();
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Picker opens and saves populate the visible list, and the clear button changes no document state.</summary>
    [Fact]
    public async Task PickerSaveAndClearUpdateVisibleHistory()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string directory = Directory.CreateTempSubdirectory("tangopad-recent-ui-").FullName;
            try
            {
                string path = Path.Combine(directory, "same.txt");
                string saved = Path.Combine(directory, "nested", "same.txt");
                _ = Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                await File.WriteAllTextAsync(path, "text").ConfigureAwait(true);
                RecentFileHistory history = new();
                EditorWindow window = new(new FileTextDocumentStore(), new Dialogs { OpenPath = path, SavePath = saved }, recentFiles: history);
                window.Show();
                Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                Guid identity = window.ViewModel.DocumentId;
                Assert.True(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
                Assert.Equal([saved, path], history.Paths);
                _ = ShowRecent(window, saved);
                Button clear = Assert.IsType<Button>(window.FindControl<Button>("ClearRecentFilesButton"));
                Click(clear);
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(history.Paths);
                Assert.Equal(identity, window.ViewModel.DocumentId);
                Assert.Equal(saved, window.ViewModel.Session.Destination);
                Assert.Equal("text", window.Editor.Text);
                Assert.False(window.ViewModel.IsDirty);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.Empty(window.RecentFilesItems.Items);
                window.Close();
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
    }

    private static Button ShowRecent(EditorWindow window, string path)
    {
        window.RecentFilesButton.Flyout!.ShowAt(window.RecentFilesButton);
        Dispatcher.UIThread.RunJobs();
        Button item = Assert.Single(window.RecentFilesItems.GetVisualDescendants().OfType<Button>(), button => Equals(button.CommandParameter, path));
        Assert.Equal($"{Path.GetFileName(path)} ({Path.GetDirectoryName(path)})", item.Content);
        Assert.Equal(path, ToolTip.GetTip(item));
        return item;
    }

    private sealed class Dialogs : IEditorDialogs
    {
        internal UnsavedDecision Decision { get; set; }
        internal string? OpenPath { get; init; }
        internal string? SavePath { get; init; }
        internal int Confirmations { get; private set; }
        internal int Pickers { get; private set; }
        internal string? Error { get; private set; }

        public Task<string?> ChooseOpenAsync() { Pickers++; return Task.FromResult(OpenPath); }
        public Task<string?> ChooseSaveAsync(string? destination) { return Task.FromResult(SavePath); }
        public Task<UnsavedDecision> ConfirmUnsavedAsync() { Confirmations++; return Task.FromResult(Decision); }
        public Task ShowErrorAsync(string message) { Error = message; return Task.CompletedTask; }
    }
}

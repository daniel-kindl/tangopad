using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Editor.App.Services;
using Editor.App.Views;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks reload against real UTF-8 files and the editor buffer.</summary>
public sealed class ReloadBindingTests
{
    /// <summary>Reload refreshes Markdown, status, history, selection, and undo, including unchanged text.</summary>
    /// <param name="sameText">Whether disk contents remain unchanged.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulReloadResetsEditingState(bool sameText)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            _ = Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "current.md");
            string settings = Path.Combine(directory, "history.json");
            try
            {
                const string original = "# Original\n";
                const string external = "# 😀\r\nλ\nlast\r";
                await File.WriteAllTextAsync(path, original).ConfigureAwait(true);
                RecentFileHistory history = new(settings);
                await history.Ready.ConfigureAwait(true);
                EditorWindow window = new(new FileTextDocumentStore(), new Dialogs(), recentFiles: history);
                window.Show();
                Assert.True(await window.ViewModel.OpenPathAsync(path).ConfigureAwait(true));
                window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Preview.Work.ConfigureAwait(true);
                window.Editor.Document.Insert(0, "edit");
                window.Editor.Document.UndoStack.Undo();
                window.Editor.Select(2, 4);
                Guid identity = window.ViewModel.DocumentId;
                string expected = sameText ? original : external;
                await File.WriteAllTextAsync(path, expected).ConfigureAwait(true);
                _ = window.Editor.TextArea.Focus();
                window.KeyPress(OperatingSystem.IsMacOS() ? Key.R : Key.F5,
                    OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.None,
                    OperatingSystem.IsMacOS() ? PhysicalKey.R : PhysicalKey.F5, null);
                window.KeyRelease(OperatingSystem.IsMacOS() ? Key.R : Key.F5,
                    OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.None,
                    OperatingSystem.IsMacOS() ? PhysicalKey.R : PhysicalKey.F5, null);
                while (window.ViewModel.IsBusy)
                {
                    await Task.Delay(10, TestContext.Current.CancellationToken).ConfigureAwait(true);
                }

                await window.Preview.Work.ConfigureAwait(true);
                Dispatcher.UIThread.RunJobs();
                Assert.NotEqual(identity, window.ViewModel.DocumentId);
                Assert.Equal(expected, window.Editor.Text);
                Assert.Equal(0, window.Editor.SelectionLength);
                Assert.Equal(0, window.Editor.CaretOffset);
                Assert.Equal(1, window.ViewModel.CaretLine);
                Assert.Equal(1, window.ViewModel.CaretColumn);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.False(window.Editor.Document.UndoStack.CanRedo);
                Assert.False(window.ViewModel.IsDirty);
                Assert.NotNull(window.Editor.SyntaxHighlighting);
                Assert.True(window.PreviewScroll.IsVisible);
                TextBlock heading = Assert.IsType<TextBlock>(window.PreviewBlocks.Children[0]);
                Assert.Equal(sameText ? "Original" : "😀", heading.Inlines!.Text);
                Assert.Equal(sameText ? "Endings: LF" : "Endings: Mixed", window.LineEndingStatus.Text);
                Assert.Equal(sameText ? "Characters: 11" : "Characters: 12", window.CharacterStatus.Text);
                Assert.Equal(sameText ? "Words: 2" : "Words: 4", window.WordStatus.Text);
                await history.FlushAsync().ConfigureAwait(true);
                RecentFileHistory persisted = new(settings);
                await persisted.Ready.ConfigureAwait(true);
                Assert.Equal([path], persisted.Paths);
                Assert.Equal(Encoding.UTF8.GetBytes(expected), await File.ReadAllBytesAsync(path).ConfigureAwait(true));
                Assert.True(await window.ViewModel.NewAsync().ConfigureAwait(true));
                Assert.False(window.PreviewScroll.IsVisible);
                Assert.Null(window.Editor.SyntaxHighlighting);
                window.Close();
            }
            finally
            {
                Directory.Delete(directory, true);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Cancellation, missing files, and invalid UTF-8 preserve selection and undo/redo history.</summary>
    /// <param name="failure">The operation outcome to exercise.</param>
    [Theory]
    [InlineData("cancel")]
    [InlineData("missing")]
    [InlineData("invalid")]
    public async Task FailedReloadPreservesEditingState(string failure)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string path = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(path, "original\r\n").ConfigureAwait(true);
                Dialogs dialogs = new() { Decision = failure == "cancel" ? UnsavedDecision.Cancel : UnsavedDecision.Discard };
                EditorWindow window = new(new FileTextDocumentStore(), dialogs);
                window.Show();
                Assert.True(await window.ViewModel.OpenPathAsync(path).ConfigureAwait(true));
                Guid identity = window.ViewModel.DocumentId;
                window.Editor.Document.Insert(0, "keep ");
                window.Editor.Document.Insert(window.Editor.Document.TextLength, "later");
                window.Editor.Document.UndoStack.Undo();
                window.Editor.Select(2, 4);
                int caret = window.Editor.CaretOffset;
                if (failure == "missing")
                {
                    File.Delete(path);
                }
                else
                {
                    await File.WriteAllBytesAsync(path, [0xff, 0xfe]).ConfigureAwait(true);
                }

                Assert.False(await window.ViewModel.ReloadAsync().ConfigureAwait(true));
                Assert.Equal(identity, window.ViewModel.DocumentId);
                Assert.Equal("keep original\r\n", window.Editor.Text);
                Assert.Equal(2, window.Editor.SelectionStart);
                Assert.Equal(4, window.Editor.SelectionLength);
                Assert.Equal(caret, window.Editor.CaretOffset);
                Assert.Equal(path, window.ViewModel.Session.Destination);
                Assert.True(window.ViewModel.IsDirty);
                Assert.True(window.Editor.Document.UndoStack.CanUndo);
                Assert.True(window.Editor.Document.UndoStack.CanRedo);
                Assert.Equal(failure != "cancel", dialogs.Error is not null);
                window.Editor.Document.UndoStack.Redo();
                Assert.Equal("keep original\r\nlater", window.Editor.Text);
                window.Editor.Document.UndoStack.Undo();
                window.Editor.Document.UndoStack.Undo();
                Assert.Equal("original\r\n", window.Editor.Text);
                Assert.False(window.ViewModel.IsDirty);
                window.Close();
            }
            finally
            {
                File.Delete(path);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private sealed class Dialogs : IEditorDialogs
    {
        internal UnsavedDecision Decision { get; init; } = UnsavedDecision.Discard;
        internal string? Error { get; private set; }
        public Task<string?> ChooseOpenAsync()
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> ChooseSaveAsync(string? destination)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<UnsavedDecision> ConfirmUnsavedAsync()
        {
            return Task.FromResult(Decision);
        }

        public Task ShowErrorAsync(string message) { Error = message; return Task.CompletedTask; }
    }
}

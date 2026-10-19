using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using Editor.App.Services;
using Editor.App.Views;
using Editor.Core;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks file drops through the window's routed drag events.</summary>
public sealed class FileDropTests
{
    /// <summary>A dropped file opens without a picker and resets the editing buffer.</summary>
    [Fact]
    public async Task DropOpensLocalMarkdownWithoutPicker()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"drop λ {Guid.NewGuid()}.md");
            try
            {
                await File.WriteAllTextAsync(path, "# Hello\r\nworld").ConfigureAwait(true);
                EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
                window.Show();
                Dispatcher.UIThread.RunJobs();
                IStorageFile file = Assert.IsType<IStorageFile>(await window.StorageProvider.TryGetFileFromPathAsync(path).ConfigureAwait(true), exactMatch: false);
                DataTransfer data = new();
                DataTransferItem item = DataTransferItem.CreateFile(file);
                item.SetText(file.Path.AbsoluteUri);
                data.Add(item);
                Assert.Equal(DragDropEffects.Copy, Send(window, DragDrop.DragEnterEvent, data).DragEffects);
                _ = Send(window, DragDrop.DropEvent, data);
                await WaitForOperationAsync(window).ConfigureAwait(true);

                Assert.Equal("# Hello\r\nworld", window.Editor.Text);
                Assert.Equal(path, window.ViewModel.Session.Destination);
                Assert.Equal([path], window.ViewModel.RecentPaths);
                Assert.Equal(DocumentFormat.Markdown, window.ViewModel.Format);
                Assert.NotNull(window.Editor.SyntaxHighlighting);
                Assert.Equal(14, window.ViewModel.CharacterCount);
                Assert.Equal(3, window.ViewModel.WordCount);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.True(window.Editor.IsKeyboardFocusWithin);
                window.Close();
            }
            finally
            {
                File.Delete(path);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Cancellation preserves the exact editing state and destination.</summary>
    [Fact]
    public async Task CancelledDropPreservesBuffer()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string path = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(path, "replacement").ConfigureAwait(true);
                Dialogs dialogs = new();
                EditorWindow window = new(new FileTextDocumentStore(), dialogs);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(await window.ViewModel.OpenPathAsync(path).ConfigureAwait(true));
                window.Editor.Text = "original";
                window.Editor.Document.Insert(0, "!");
                window.Editor.Select(2, 3);
                int caret = window.Editor.CaretOffset;
                Guid identity = window.ViewModel.DocumentId;
                IStorageFile file = Assert.IsType<IStorageFile>(await window.StorageProvider.TryGetFileFromPathAsync(path).ConfigureAwait(true), exactMatch: false);
                DataTransfer data = new();
                DataTransferItem item = DataTransferItem.CreateFile(file);
                item.SetText(file.Path.AbsoluteUri);
                data.Add(item);
                Assert.Equal(DragDropEffects.Copy, Send(window, DragDrop.DragEnterEvent, data).DragEffects);
                _ = Send(window, DragDrop.DropEvent, data);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, dialogs.Confirmations);
                Assert.Equal(identity, window.ViewModel.DocumentId);
                Assert.Equal(path, window.ViewModel.Session.Destination);
                Assert.Equal("!original", window.Editor.Text);
                Assert.Equal(2, window.Editor.SelectionStart);
                Assert.Equal(3, window.Editor.SelectionLength);
                Assert.Equal(caret, window.Editor.CaretOffset);
                Assert.True(window.ViewModel.IsDirty);
                window.Editor.Document.UndoStack.Undo();
                Assert.Equal("original", window.Editor.Text);
                dialogs.Decision = UnsavedDecision.Discard;
                window.Close();
            }
            finally
            {
                File.Delete(path);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Rejected data cannot insert text or alter the selection, history, or session.</summary>
    /// <param name="kind">The unsupported drag payload.</param>
    [Theory]
    [InlineData("empty")]
    [InlineData("url")]
    [InlineData("file-uri-text")]
    [InlineData("multiple")]
    [InlineData("folder")]
    [InlineData("move-only")]
    public async Task UnsupportedDropsPreserveState(string kind)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string path = Path.GetTempFileName();
            try
            {
                Dialogs dialogs = new();
                EditorWindow window = new(new FileTextDocumentStore(), dialogs);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.Editor.Text = "original";
                window.Editor.Document.Insert(0, "!");
                window.Editor.Select(2, 3);
                int caret = window.Editor.CaretOffset;
                Guid identity = window.ViewModel.DocumentId;
                IStorageFile file = Assert.IsType<IStorageFile>(await window.StorageProvider.TryGetFileFromPathAsync(path).ConfigureAwait(true), exactMatch: false);
                DataTransfer data = new();
                switch (kind)
                {
                    case "url": data.Add(DataTransferItem.CreateText("https://example.com/a.md")); break;
                    case "file-uri-text": data.Add(DataTransferItem.CreateText(new Uri(path).AbsoluteUri)); break;
                    case "multiple": data.Add(DataTransferItem.CreateFile(file)); data.Add(DataTransferItem.CreateFile(file)); break;
                    case "folder":
                        IStorageFolder folder = Assert.IsType<IStorageFolder>(await window.StorageProvider.TryGetFolderFromPathAsync(Path.GetTempPath()).ConfigureAwait(true), exactMatch: false);
                        data.Add(DataTransferItem.CreateFile(folder));
                        break;
                    case "move-only": data.Add(DataTransferItem.CreateFile(file)); break;
                    default:
                        break;
                }

                DragDropEffects effects = kind == "move-only" ? DragDropEffects.Move : DragDropEffects.Copy;
                Assert.Equal(DragDropEffects.None, Send(window, DragDrop.DragEnterEvent, data, effects).DragEffects);
                Assert.False(window.DropHint.IsVisible);
                Assert.Equal(DragDropEffects.None, Send(window, DragDrop.DropEvent, data, effects).DragEffects);
                Assert.Equal(0, dialogs.Confirmations);
                Assert.Equal(identity, window.ViewModel.DocumentId);
                Assert.Equal("!original", window.Editor.Text);
                Assert.Equal(2, window.Editor.SelectionStart);
                Assert.Equal(3, window.Editor.SelectionLength);
                Assert.Equal(caret, window.Editor.CaretOffset);
                Assert.True(window.ViewModel.IsDirty);
                Assert.Null(window.ViewModel.Session.Destination);
                window.Editor.Document.UndoStack.Undo();
                Assert.Equal("original", window.Editor.Text);
                dialogs.Decision = UnsavedDecision.Discard;
                window.Close();
            }
            finally
            {
                File.Delete(path);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Save and Discard use the same replacement workflow as picker Open.</summary>
    /// <param name="save">Whether to save the dirty document before replacement.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirtyDropSavesOrDiscards(bool save)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string original = Path.Combine(Path.GetTempPath(), $"drop-{Guid.NewGuid()}.md");
            string next = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(original, "original").ConfigureAwait(true);
                await File.WriteAllTextAsync(next, "replacement").ConfigureAwait(true);
                Dialogs dialogs = new() { Decision = save ? UnsavedDecision.Save : UnsavedDecision.Discard };
                EditorWindow window = new(new FileTextDocumentStore(), dialogs);
                window.Show();
                Assert.True(await window.ViewModel.OpenPathAsync(original).ConfigureAwait(true));
                Assert.NotNull(window.Editor.SyntaxHighlighting);
                window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Preview.Work.ConfigureAwait(true);
                Assert.True(window.PreviewScroll.IsVisible);
                window.Editor.Document.Insert(0, "!");
                window.ViewModel.Search.ShowFindCommand.Execute(null);
                window.ViewModel.Search.Query = "replacement";
                _ = window.QueryInput.Focus();
                IStorageFile file = Assert.IsType<IStorageFile>(await window.StorageProvider.TryGetFileFromPathAsync(next).ConfigureAwait(true), exactMatch: false);
                DataTransfer data = new();
                data.Add(DataTransferItem.CreateFile(file));
                _ = Send(window, DragDrop.DropEvent, data);
                await WaitForOperationAsync(window).ConfigureAwait(true);
                Assert.Equal(1, dialogs.Confirmations);
                Assert.Equal("replacement", window.Editor.Text);
                Assert.Equal(next, window.ViewModel.Session.Destination);
                Assert.Equal(DocumentFormat.PlainText, window.ViewModel.Format);
                Assert.Null(window.Editor.SyntaxHighlighting);
                Assert.False(window.PreviewScroll.IsVisible);
                Assert.False(window.PreviewToggle.IsVisible);
                Assert.Equal(save ? "!original" : "original", await File.ReadAllTextAsync(original).ConfigureAwait(true));
                Assert.False(window.ViewModel.IsDirty);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.Equal(1, window.ViewModel.Search.MatchCount);
                Assert.True(window.Editor.IsKeyboardFocusWithin);
                window.Close();
            }
            finally
            {
                File.Delete(original);
                File.Delete(next);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>A file that disappears or has invalid UTF-8 cannot replace the active document.</summary>
    /// <param name="missing">Whether the file disappears after drag validation.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedDropPreservesActiveDocument(bool missing)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string path = Path.GetTempFileName();
            try
            {
                await File.WriteAllBytesAsync(path, [0xff, 0xfe]).ConfigureAwait(true);
                Dialogs dialogs = new() { Decision = UnsavedDecision.Discard, AllowError = true };
                EditorWindow window = new(new FileTextDocumentStore(), dialogs);
                window.Show();
                window.Editor.Text = "original";
                window.Editor.Document.Insert(0, "!");
                window.Editor.Select(2, 3);
                int caret = window.Editor.CaretOffset;
                Guid identity = window.ViewModel.DocumentId;
                IStorageFile file = Assert.IsType<IStorageFile>(await window.StorageProvider.TryGetFileFromPathAsync(path).ConfigureAwait(true), exactMatch: false);
                DataTransfer data = new();
                data.Add(DataTransferItem.CreateFile(file));
                Assert.Equal(DragDropEffects.Copy, Send(window, DragDrop.DragOverEvent, data).DragEffects);
                Assert.True(window.DropHint.IsVisible);
                _ = Send(window, DragDrop.DragLeaveEvent, data);
                Assert.False(window.DropHint.IsVisible);
                if (missing)
                {
                    File.Delete(path);
                }

                _ = Send(window, DragDrop.DropEvent, data);
                await WaitForOperationAsync(window).ConfigureAwait(true);
                Assert.NotNull(dialogs.Error);
                Assert.Equal(identity, window.ViewModel.DocumentId);
                Assert.Equal("!original", window.Editor.Text);
                Assert.Null(window.ViewModel.Session.Destination);
                Assert.Equal(2, window.Editor.SelectionStart);
                Assert.Equal(3, window.Editor.SelectionLength);
                Assert.Equal(caret, window.Editor.CaretOffset);
                Assert.True(window.ViewModel.IsDirty);
                window.Editor.Document.UndoStack.Undo();
                Assert.Equal("original", window.Editor.Text);
                window.Close();
            }
            finally
            {
                File.Delete(path);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Dragging an editor selection retains the editor's text-drop behavior.</summary>
    [Fact]
    public async Task EditorSelectionDragRetainsTextDropAndUndo()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            Dialogs dialogs = new() { Decision = UnsavedDecision.Discard };
            EditorWindow window = new(new FileTextDocumentStore(), dialogs);
            window.Show();
            window.Editor.Text = "abc";
            window.Editor.Select(0, 3);
            Dispatcher.UIThread.RunJobs();
            DataTransfer data = window.Editor.TextArea.Selection.CreateDataObject(window.Editor.TextArea);
            window.Editor.TextArea.RaiseEvent(new DataObjectCopyingEventArgs(data, true));
            Point end = new(200, 10);
            DragEventArgs args = new(DragDrop.DropEvent, data, window.Editor.TextArea.TextView, end, KeyModifiers.None)
            {
                DragEffects = DragDropEffects.Copy,
            };
            window.Editor.TextArea.RaiseEvent(args);
            Assert.Equal("abcabc", window.Editor.Text);
            window.Editor.Document.UndoStack.Undo();
            Assert.Equal("abc", window.Editor.Text);
            Assert.Equal(0, dialogs.Confirmations);
            window.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static async Task WaitForOperationAsync(EditorWindow window)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (window.ViewModel.IsBusy)
        {
            await Task.Delay(10, timeout.Token).ConfigureAwait(true);
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static DragEventArgs Send(EditorWindow window, RoutedEvent<DragEventArgs> routedEvent, IDataTransfer data, DragDropEffects effects = DragDropEffects.Copy)
    {
        DragEventArgs args = new(routedEvent, data, window.Editor.TextArea, new Point(100, 120), KeyModifiers.None)
        {
            DragEffects = effects,
        };
        window.Editor.TextArea.RaiseEvent(args);
        return args;
    }

    private sealed class Dialogs : IEditorDialogs
    {
        internal int Confirmations { get; private set; }
        internal UnsavedDecision Decision { get; set; } = UnsavedDecision.Cancel;
        internal bool AllowError { get; init; }
        internal string? Error { get; private set; }
        public Task<string?> ChooseOpenAsync() { Assert.Fail("A drop must not open the picker."); return Task.FromResult<string?>(null); }
        public Task<string?> ChooseSaveAsync(string? destination) { return Task.FromResult<string?>(null); }
        public Task<UnsavedDecision> ConfirmUnsavedAsync() { Confirmations++; return Task.FromResult(Decision); }
        public Task ShowErrorAsync(string message) { Error = message; Assert.True(AllowError, message); return Task.CompletedTask; }
    }
}

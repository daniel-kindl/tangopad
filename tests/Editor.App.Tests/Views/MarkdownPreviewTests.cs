using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Editor.App.Services;
using Editor.App.Views;
using Editor.Application;
using Editor.Formats.Markdown;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks preview behavior through the real editor window.</summary>
public sealed class MarkdownPreviewTests
{
    /// <summary>Preview is opt-in, follows session format, and preserves editing and persistence.</summary>
    [Fact]
    public async Task ToggleAndSaveTransitionsPreserveSource()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            Store store = new();
            Dialogs dialogs = new() { Path = "notes.md" };
            EditorWindow window = new(store, dialogs);
            window.Show();
            Button toggle = Assert.IsType<Button>(window.FindControl<Button>("PreviewToggle"));
            ScrollViewer preview = Assert.IsType<ScrollViewer>(window.FindControl<ScrollViewer>("PreviewScroll"));
            Assert.False(toggle.IsVisible);
            Assert.False(preview.IsVisible);
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            Assert.True(toggle.IsVisible);
            Assert.False(preview.IsVisible);
            _ = toggle.Focus();
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.Editor.IsKeyboardFocusWithin);
            Assert.True(preview.IsVisible);
            Assert.Equal("Hide Preview", toggle.Content);
            window.Editor.Document.Insert(window.Editor.Document.TextLength, "\n**edit** 🥔");
            window.Editor.Select(2, 3);
            int caret = window.Editor.CaretOffset;
            foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = theme;
                Dispatcher.UIThread.RunJobs();
                Assert.True(preview.IsVisible);
                Assert.Equal(2, window.Editor.SelectionStart);
                Assert.Equal(3, window.Editor.SelectionLength);
                Assert.Equal(caret, window.Editor.CaretOffset);
                Assert.True(window.ViewModel.IsDirty);
            }

            dialogs.Path = "notes.txt";
            store.Failure = true;
            Assert.False(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
            Assert.True(preview.IsVisible);
            store.Failure = false;
            TaskCompletionSource saved = new();
            store.Save = saved.Task;
            Task<bool> cancelledSave = window.ViewModel.SaveAsAsync();
            window.ViewModel.Cancel();
            Assert.False(await cancelledSave.ConfigureAwait(true));
            Assert.True(preview.IsVisible);
            store.Save = Task.CompletedTask;
            dialogs.Path = null;
            Assert.False(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
            Assert.True(preview.IsVisible);
            dialogs.Path = "notes.txt";
            Assert.True(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
            Assert.False(toggle.IsVisible);
            Assert.False(preview.IsVisible);
            Assert.Equal("# Heading\r\nsource\n**edit** 🥔", store.SavedText);
            Assert.False(window.ViewModel.IsDirty);
            Assert.Equal(2, window.Editor.SelectionStart);
            Assert.Equal(3, window.Editor.SelectionLength);
            Assert.Equal(caret, window.Editor.CaretOffset);
            window.Editor.Document.UndoStack.Undo();
            Assert.Equal("# Heading\r\nsource", window.Editor.Text);
            window.Editor.Document.UndoStack.Redo();
            dialogs.Path = "again.md";
            Assert.True(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
            Assert.True(toggle.IsVisible);
            Assert.False(preview.IsVisible);
            _ = toggle.Focus();
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.Editor.IsKeyboardFocusWithin);
            Assert.True(preview.IsVisible);
            Assert.True(await window.ViewModel.NewAsync().ConfigureAwait(true));
            Assert.False(preview.IsVisible);
            Assert.False(toggle.IsVisible);
            window.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Live rendering stays inert and does not change text, bytes, selection, or undo.</summary>
    [Fact]
    public async Task LiveRenderingIsReadOnlyAndOffline()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string localFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(localFile, "FILE CONTENT MUST NOT APPEAR", TestContext.Current.CancellationToken).ConfigureAwait(true);
                Store store = new();
                EditorWindow window = new(store, new Dialogs { Path = "notes.md" });
                window.Show();
                Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                string source = $"# Live\r\n**strong** and *emphasis* `code`\n\n![remote](http://127.0.0.1:{port}/image) [link](http://127.0.0.1:{port}/link)\n\n![local]({new Uri(localFile).AbsoluteUri})\n\n<script>alert(1)</script>";
                window.Editor.Document.Insert(0, source);
                window.Editor.Select(1, 4);
                int caret = window.Editor.CaretOffset;
                await window.Preview.Work.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ConfigureAwait(true);
                Assert.All(window.PreviewBlocks.Children, child => Assert.IsType<TextBlock>(child));
                TextBlock heading = Assert.IsType<TextBlock>(window.PreviewBlocks.Children[0]);
                Assert.Equal("Live", heading.Inlines!.Text);
                Assert.Contains("preview-heading", heading.Classes);
                string rendered = string.Concat(window.PreviewBlocks.Children.Cast<TextBlock>().Select(block => block.Inlines!.Text));
                Assert.Contains("<script>alert(1)</script>", rendered, StringComparison.Ordinal);
                Assert.DoesNotContain("FILE CONTENT MUST NOT APPEAR", rendered, StringComparison.Ordinal);
                Assert.False(listener.Pending());
                foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    window.RequestedThemeVariant = theme;
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(window.FindResource(window.ActualThemeVariant, "Brush.Markdown.Heading"), heading.Foreground);
                }
                Assert.Equal(source + "# Heading\r\nsource", window.Editor.Text);
                Assert.Equal(1, window.Editor.SelectionStart);
                Assert.Equal(4, window.Editor.SelectionLength);
                Assert.Equal(caret, window.Editor.CaretOffset);
                Assert.True(window.ViewModel.IsDirty);
                Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                Assert.Equal(source + "# Heading\r\nsource", store.SavedText);
                Assert.False(window.ViewModel.IsDirty);
                window.Editor.Document.UndoStack.Undo();
                Assert.Equal("# Heading\r\nsource", window.Editor.Text);
                window.Editor.Document.UndoStack.Redo();
                Assert.Equal(source + "# Heading\r\nsource", window.Editor.Text);
                Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                window.Close();
            }
            finally
            {
                File.Delete(localFile);
            }
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Preview failures preserve editing and allow the next request to succeed.</summary>
    /// <param name="failure">The failing preview operation.</param>
    /// <param name="retry">The action that requests another preview.</param>
    [Theory]
    [InlineData("parse", "edit")]
    [InlineData("cancel", "edit")]
    [InlineData("render", "edit")]
    [InlineData("parse", "hide")]
    [InlineData("parse", "open")]
    [InlineData("parse", "new")]
    [InlineData("parse", "plain")]
    public async Task RecoversFromFailure(string failure, string retry)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            int calls = 0;
            Store store = new();
            Dialogs dialogs = new() { Path = "notes.md" };
            EditorWindow window = new(store, dialogs, source =>
            {
                return (Interlocked.Increment(ref calls), failure) switch
                {
                    (1, "cancel") => throw new OperationCanceledException("Parser cancellation."),
                    (1, "render") => new PreviewDocument([
                        new PreviewBlock([new PreviewRun("Partial output")]),
                        new PreviewBlock([], HeadingLevel: 7),
                    ], false),
                    (1, _) => throw new InvalidOperationException("Parser failure."),
                    _ => MarkdownPreviewParser.Parse(source),
                };
            });
            window.Show();
            try
            {
                Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                Guid identity = window.ViewModel.DocumentId;
                window.Editor.Document.Insert(0, "# Edited\n");
                window.Editor.Select(2, 3);
                int caret = window.Editor.CaretOffset;
                window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Preview.Work.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ConfigureAwait(true);
                Assert.True(window.Preview.Work.IsCompletedSuccessfully);
                Assert.Equal(1, calls);
                TextBlock error = Assert.IsType<TextBlock>(Assert.Single(window.PreviewBlocks.Children));
                Assert.Equal("Preview unavailable. Edit the source or hide and show the preview to try again.", error.Text);
                Assert.Equal(error.Text, AutomationProperties.GetName(error));
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(error));
                Assert.Contains("preview-block", error.Classes);
                Assert.Equal(identity, window.ViewModel.DocumentId);
                Assert.True(window.ViewModel.IsDirty);
                Assert.Equal("# Edited\n# Heading\r\nsource", window.Editor.Text);
                Assert.Equal(2, window.Editor.SelectionStart);
                Assert.Equal(3, window.Editor.SelectionLength);
                Assert.Equal(caret, window.Editor.CaretOffset);
                Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                Assert.Equal("# Edited\n# Heading\r\nsource", store.SavedText);
                window.Editor.Document.UndoStack.Undo();
                Assert.Equal("# Heading\r\nsource", window.Editor.Text);
                window.Editor.Document.UndoStack.Redo();
                Assert.Equal("# Edited\n# Heading\r\nsource", window.Editor.Text);

                switch (retry)
                {
                    case "edit":
                        window.Editor.Document.Insert(0, "# Recovered\n");
                        break;
                    case "hide":
                        window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;
                    case "open":
                        store.LoadedText = "# Reopened";
                        Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                        break;
                    case "new":
                        Assert.True(await window.ViewModel.NewAsync().ConfigureAwait(true));
                        dialogs.Path = "new.md";
                        Assert.True(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
                        window.Editor.Document.Insert(0, "# New document");
                        window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;
                    case "plain":
                        dialogs.Path = "notes.txt";
                        Assert.True(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
                        dialogs.Path = "notes.md";
                        Assert.True(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
                        window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;
                    default:
                        throw new InvalidOperationException("Unknown retry action.");
                }
                Assert.Empty(window.PreviewBlocks.Children);
                await window.Preview.Work.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ConfigureAwait(true);
                Assert.True(window.Preview.Work.IsCompletedSuccessfully);
                TextBlock heading = Assert.IsType<TextBlock>(window.PreviewBlocks.Children[0]);
                Assert.Equal(retry switch
                {
                    "edit" => "Recovered",
                    "open" => "Reopened",
                    "new" => "New document",
                    _ => "Edited",
                }, heading.Inlines!.Text);
            }
            finally
            {
                Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Work remains serial and stale results cannot cross document or view transitions.</summary>
    /// <param name="transition">The transition while the first parse is blocked.</param>
    /// <param name="outcome">The result of the blocked parser.</param>
    [Theory]
    [InlineData("edit", "success")]
    [InlineData("edit", "failure")]
    [InlineData("edit", "cancel")]
    [InlineData("open", "success")]
    [InlineData("open", "failure")]
    [InlineData("open", "cancel")]
    [InlineData("hide", "success")]
    [InlineData("hide", "failure")]
    [InlineData("hide", "cancel")]
    [InlineData("plain", "success")]
    [InlineData("plain", "failure")]
    [InlineData("plain", "cancel")]
    [InlineData("close", "success")]
    [InlineData("close", "failure")]
    [InlineData("close", "cancel")]
    public async Task DiscardsStaleWork(string transition, string outcome)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            using ManualResetEventSlim release = new();
            TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            int active = 0;
            Store store = new();
            Dialogs dialogs = new() { Path = "notes.md" };
            EditorWindow window = new(store, dialogs, source =>
            {
                Assert.False(Dispatcher.UIThread.CheckAccess());
                Assert.Equal(1, Interlocked.Increment(ref active));
                if (Interlocked.Increment(ref calls) == 1)
                {
                    started.SetResult();
                    Assert.True(release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
                    if (outcome != "success")
                    {
                        _ = Interlocked.Decrement(ref active);
                        throw outcome == "cancel"
                            ? new OperationCanceledException("Stale parser cancellation.")
                            : new InvalidOperationException("Stale parser failure.");
                    }
                }
                _ = Interlocked.Decrement(ref active);
                return MarkdownPreviewParser.Parse(source);
            });
            window.Show();
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ConfigureAwait(true);
            try
            {
                switch (transition)
                {
                    case "edit":
                        for (int i = 0; i < 20; i++)
                        {
                            window.Editor.Text = "# Latest " + i;
                        }
                        break;
                    case "open":
                        store.LoadedText = "# Other document";
                        Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                        break;
                    case "hide":
                        window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;
                    case "plain":
                        dialogs.Path = "notes.txt";
                        Assert.True(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
                        break;
                    default:
                        throw new InvalidOperationException("Unknown transition.");
                    case "close":
                        window.Close();
                        Dispatcher.UIThread.RunJobs();
                        break;
                }
            }
            finally
            {
                release.Set();
            }
            await window.Preview.Work.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ConfigureAwait(true);
            if (transition is "edit" or "open")
            {
                Assert.Equal(2, calls);
                TextBlock heading = Assert.IsType<TextBlock>(window.PreviewBlocks.Children[0]);
                Assert.Equal(transition == "edit" ? "Latest 19" : "Other document", heading.Inlines!.Text);
            }
            else
            {
                Assert.Equal(1, calls);
                Assert.Empty(window.PreviewBlocks.Children);
            }
            if (transition != "close")
            {
                Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                window.Close();
            }
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Preview preserves exact saved bytes and degrades safely for oversized documents.</summary>
    [Fact]
    public async Task PreservesBytesAndHandlesLargeInput()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".md");
            const string source = "# Héading\r\n\n**text**\r🥔 e\u0301\n";
            byte[] expected = Encoding.UTF8.GetBytes(source);
            try
            {
                await File.WriteAllBytesAsync(path, expected, TestContext.Current.CancellationToken).ConfigureAwait(true);
                EditorWindow window = new(new FileTextDocumentStore(), new Dialogs { Path = path });
                window.Show();
                Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Preview.Work.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ConfigureAwait(true);
                Assert.False(window.ViewModel.IsDirty);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                Assert.Equal(expected, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true));
                window.Editor.Document.Insert(window.Editor.Document.TextLength, new string('x', 100_000));
                await window.Preview.Work.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ConfigureAwait(true);
                TextBlock notice = Assert.IsType<TextBlock>(Assert.Single(window.PreviewBlocks.Children));
                Assert.StartsWith("Preview limit reached.", notice.Text, StringComparison.Ordinal);
                Assert.True(window.ViewModel.IsDirty);
                window.Editor.Document.Insert(0, "still editable");
                window.Editor.Document.UndoStack.Undo();
                window.Editor.Document.UndoStack.Undo();
                Assert.Equal(source, window.Editor.Text);
                Assert.False(window.ViewModel.IsDirty);
                window.Close();
                await window.Preview.Work.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ConfigureAwait(true);
                Assert.Empty(window.PreviewBlocks.Children);
            }
            finally
            {
                File.Delete(path);
            }
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Rapid edits use the newest text and closing cancels a pending debounce delay.</summary>
    [Fact]
    public async Task DebouncesAndCancelsBeforeParsing()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            int calls = 0;
            EditorWindow window = new(new Store(), new Dialogs { Path = "notes.md" }, source =>
            {
                _ = Interlocked.Increment(ref calls);
                return MarkdownPreviewParser.Parse(source);
            });
            window.Show();
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 20; i++)
            {
                window.Editor.Text = "# Latest " + i;
            }
            await window.Preview.Work.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Equal(1, calls);
            Assert.Equal("Latest 19", Assert.IsType<TextBlock>(Assert.Single(window.PreviewBlocks.Children)).Inlines!.Text);
            window.Editor.Text = "# Pending";
            Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
            window.Close();
            Dispatcher.UIThread.RunJobs();
            await window.Preview.Work.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Equal(1, calls);
            Assert.Empty(window.PreviewBlocks.Children);
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private sealed class Store : ITextDocumentStore
    {
        internal string LoadedText { get; set; } = "# Heading\r\nsource";
        internal Task Save { get; set; } = Task.CompletedTask;
        internal bool Failure { get; set; }
        internal string? SavedText { get; private set; }
        public Task<string> LoadAsync(string destination, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LoadedText);
        }

        public Task SaveAsync(string destination, string text, CancellationToken cancellationToken = default)
        {
            SavedText = text;
            return Failure ? Task.FromException(new IOException("Save failed.")) : Save.WaitAsync(cancellationToken);
        }
    }

    private sealed class Dialogs : IEditorDialogs
    {
        internal string? Path { get; set; }
        public Task<string?> ChooseOpenAsync() { return Task.FromResult(Path); }
        public Task<string?> ChooseSaveAsync(string? destination) { return Task.FromResult(Path); }
        public Task<UnsavedDecision> ConfirmUnsavedAsync() { return Task.FromResult(UnsavedDecision.Discard); }
        public Task ShowErrorAsync(string message) { return Task.CompletedTask; }
    }
}

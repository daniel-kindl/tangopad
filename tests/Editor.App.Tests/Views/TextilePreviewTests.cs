using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Editor.App.Services;
using Editor.App.Views;
using Editor.Application;
using Editor.Core;
using Editor.Formats.Markdown;
using Editor.Formats.Textile;
using Editor.Infrastructure;
using Editor.Preview;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks inert Textile previews through the native editor controls.</summary>
public sealed class TextilePreviewTests
{
    /// <summary>Textile styles and tables render without resource access or source changes.</summary>
    [Fact]
    public async Task RendersTextAndTablesOfflineInBothThemes()
    {
        await OnUiAsync(async () =>
        {
            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string asset = Path.GetTempFileName();
            string path = Path.Combine(Path.GetTempPath(), $"preview-{Guid.NewGuid()}.textile");
            try
            {
                await File.WriteAllTextAsync(asset, "LOCAL ASSET MUST NOT APPEAR", TestContext.Current.CancellationToken).ConfigureAwait(true);
                string source = $"h1. Héading\r\n\r\n*strong* _italic_ +underlined+ -struck- @code@\n\n|_. Name|_. Value|\n|one|two|\n\n\"remote label\":http://127.0.0.1:{port}/link !http://127.0.0.1:{port}/image(remote image)! !{new Uri(asset).AbsoluteUri}(local image)!\n\n<script>alert(1)</script>\n\np{{color:red}}. styled text\n";
                byte[] expected = Encoding.UTF8.GetBytes(source);
                await File.WriteAllBytesAsync(path, expected, TestContext.Current.CancellationToken).ConfigureAwait(true);
                EditorWindow window = new(new FileTextDocumentStore(), new Dialogs { Path = path });
                window.Show();
                Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                Assert.True(window.PreviewToggle.IsVisible);
                Assert.False(window.PreviewScroll.IsVisible);
                Assert.Equal("Textile preview", AutomationProperties.GetName(window.PreviewScroll));
                Toggle(window);
                await WaitForWorkAsync(window).ConfigureAwait(true);

                TextBlock heading = Assert.IsType<TextBlock>(window.PreviewBlocks.Children[0]);
                Assert.Equal("Héading", heading.Inlines!.Text);
                TextBlock paragraph = Assert.IsType<TextBlock>(window.PreviewBlocks.Children[1]);
                Run[] runs = [.. paragraph.Inlines!.OfType<Run>()];
                Assert.Contains(runs, run => run.Text == "strong" && run.FontWeight == FontWeight.Bold);
                Assert.Contains(runs, run => run.Text == "italic" && run.FontStyle == FontStyle.Italic);
                Run underlined = Assert.Single(runs, run => run.Text == "underlined");
                Assert.Contains(underlined.TextDecorations!, decoration => decoration.Location == TextDecorationLocation.Underline);
                Run struck = Assert.Single(runs, run => run.Text == "struck");
                Assert.Contains(struck.TextDecorations!, decoration => decoration.Location == TextDecorationLocation.Strikethrough);
                Assert.Contains(runs, run => run.Text == "code" && run.FontFamily == (FontFamily)window.FindResource("Font.Editor")!);
                Grid table = Assert.IsType<Grid>(window.PreviewBlocks.Children[2]);
                Assert.Equal(2, table.RowDefinitions.Count);
                Assert.Equal(2, table.ColumnDefinitions.Count);
                TextBlock[] cells = [.. table.Children.Cast<Border>().Select(border => Assert.IsType<TextBlock>(border.Child))];
                Assert.Equal(["Name", "Value", "one", "two"], cells.Select(cell => cell.Inlines!.Text));
                Assert.Equal(FontWeight.Bold, Assert.IsType<Run>(cells[0].Inlines![0]).FontWeight);
                string rendered = string.Concat(window.PreviewBlocks.Children.OfType<TextBlock>().Select(block => block.Inlines!.Text));
                Assert.Contains("remote label", rendered, StringComparison.Ordinal);
                Assert.Contains("remote image", rendered, StringComparison.Ordinal);
                Assert.Contains("local image", rendered, StringComparison.Ordinal);
                Assert.Contains("<script>alert(1)</script>", rendered, StringComparison.Ordinal);
                Assert.Contains("{color:red}", rendered, StringComparison.Ordinal);
                Assert.DoesNotContain("LOCAL ASSET MUST NOT APPEAR", rendered, StringComparison.Ordinal);
                Assert.False(listener.Pending());
                foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    window.RequestedThemeVariant = theme;
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(window.FindResource(theme, "Brush.Textile.Heading"), heading.Foreground);
                    Assert.All(window.PreviewBlocks.Children, child => Assert.True(child is TextBlock or Grid));
                }
                Assert.Equal(source, window.Editor.Text);
                Assert.False(window.ViewModel.IsDirty);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                Assert.True(window.PreviewScroll.IsVisible);
                Assert.Equal(expected, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true));
                window.Editor.Document.Insert(window.Editor.Document.TextLength, "p. edit 🥔\n");
                window.Editor.Select(2, 3);
                int caret = window.Editor.CaretOffset;
                await WaitForWorkAsync(window).ConfigureAwait(true);
                Assert.True(window.ViewModel.IsDirty);
                Assert.Equal(2, window.Editor.SelectionStart);
                Assert.Equal(3, window.Editor.SelectionLength);
                Assert.Equal(caret, window.Editor.CaretOffset);
                Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                Assert.True(window.PreviewScroll.IsVisible);
                Assert.Equal(Encoding.UTF8.GetBytes(source + "p. edit 🥔\n"), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true));
                window.Editor.Document.UndoStack.Undo();
                Assert.Equal(source, window.Editor.Text);
                window.Editor.Document.UndoStack.Redo();
                Assert.Equal(source + "p. edit 🥔\n", window.Editor.Text);
                Close(window);
            }
            finally
            {
                File.Delete(asset);
                File.Delete(path);
            }
        }).ConfigureAwait(true);
    }

    /// <summary>Format transitions select separate parsers and reset the preview option.</summary>
    [Fact]
    public async Task SelectsOnlyTheCurrentFormatParser()
    {
        await OnUiAsync(async () =>
        {
            List<DocumentFormat> formats = [];
            Store store = new();
            Dialogs dialogs = new() { Path = "notes.textile" };
            EditorWindow window = new(store, dialogs, (format, source) =>
            {
                formats.Add(format);
                return Parse(format, source);
            });
            window.Show();
            Assert.False(window.PreviewToggle.IsVisible);
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            Toggle(window);
            await WaitForWorkAsync(window).ConfigureAwait(true);
            Assert.Equal("Heading", Assert.IsType<TextBlock>(Assert.Single(window.PreviewBlocks.Children)).Inlines!.Text);
            dialogs.Path = "notes.md";
            Assert.True(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
            Assert.True(window.PreviewToggle.IsVisible);
            Assert.False(window.PreviewScroll.IsVisible);
            Assert.Empty(window.PreviewBlocks.Children);
            Assert.Equal("Markdown preview", AutomationProperties.GetName(window.PreviewScroll));
            Toggle(window);
            await WaitForWorkAsync(window).ConfigureAwait(true);
            Assert.Equal("h1. Heading", Assert.IsType<TextBlock>(Assert.Single(window.PreviewBlocks.Children)).Inlines!.Text);
            dialogs.Path = "notes.txt";
            Assert.True(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
            Assert.False(window.PreviewToggle.IsVisible);
            Assert.False(window.PreviewScroll.IsVisible);
            Assert.Empty(window.PreviewBlocks.Children);
            Assert.Equal([DocumentFormat.Textile, DocumentFormat.Markdown], formats);
            Close(window);
        }).ConfigureAwait(true);
    }

    /// <summary>A later request recovers from parsing, cancellation, or rendering failure.</summary>
    /// <param name="failure">The failing preview stage.</param>
    [Theory]
    [InlineData("parse")]
    [InlineData("cancel")]
    [InlineData("render")]
    public async Task RecoversAfterFailure(string failure)
    {
        await OnUiAsync(async () =>
        {
            int calls = 0;
            EditorWindow window = new(new Store(), new Dialogs { Path = "notes.textile" }, (format, source) =>
            {
                return (Interlocked.Increment(ref calls), failure) switch
                {
                    (1, "cancel") => throw new OperationCanceledException("Parser cancellation."),
                    (1, "render") => new PreviewDocument([new PreviewTextBlock([new PreviewRun("Partial")]), new PreviewTextBlock([], HeadingLevel: 7)], false),
                    (1, _) => throw new InvalidOperationException("Parser failure."),
                    _ => Parse(format, source),
                };
            });
            window.Show();
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            Toggle(window);
            await WaitForWorkAsync(window).ConfigureAwait(true);
            TextBlock error = Assert.IsType<TextBlock>(Assert.Single(window.PreviewBlocks.Children));
            Assert.Equal("Preview unavailable. Edit the source or hide and show the preview to try again.", error.Text);
            Assert.Equal(error.Text, AutomationProperties.GetName(error));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(error));
            Assert.Equal("h1. Heading", window.Editor.Text);
            Assert.False(window.ViewModel.IsDirty);
            window.Editor.Text = "h2. Recovered";
            await WaitForWorkAsync(window).ConfigureAwait(true);
            Assert.Equal("Recovered", Assert.IsType<TextBlock>(Assert.Single(window.PreviewBlocks.Children)).Inlines!.Text);
            Assert.Equal(2, calls);
            Assert.True(window.ViewModel.IsDirty);
            Close(window);
        }).ConfigureAwait(true);
    }

    /// <summary>Stale parser success, failure, and cancellation cannot replace current output.</summary>
    /// <param name="transition">The operation while parsing remains blocked.</param>
    /// <param name="outcome">The blocked parser result.</param>
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
    [InlineData("markdown", "success")]
    [InlineData("markdown", "failure")]
    [InlineData("markdown", "cancel")]
    [InlineData("close", "success")]
    [InlineData("close", "failure")]
    [InlineData("close", "cancel")]
    public async Task DiscardsStaleWork(string transition, string outcome)
    {
        await OnUiAsync(async () =>
        {
            using ManualResetEventSlim release = new();
            TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            int active = 0;
            Store store = new();
            Dialogs dialogs = new() { Path = "notes.textile" };
            EditorWindow window = new(store, dialogs, (format, source) =>
            {
                Assert.False(Dispatcher.UIThread.CheckAccess());
                Assert.Equal(1, Interlocked.Increment(ref active));
                try
                {
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        started.SetResult();
                        Assert.True(release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
                        if (outcome != "success")
                        {
                            throw outcome == "cancel" ? new OperationCanceledException("Stale cancellation.") : new InvalidOperationException("Stale failure.");
                        }
                    }
                    return Parse(format, source);
                }
                finally { _ = Interlocked.Decrement(ref active); }
            });
            window.Show();
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            Toggle(window);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ConfigureAwait(true);
            try
            {
                switch (transition)
                {
                    case "edit":
                        for (int edit = 0; edit < 20; edit++) { window.Editor.Text = $"h1. Latest {edit}"; }
                        break;
                    case "open":
                        store.LoadedText = "h1. Other document";
                        Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                        break;
                    case "hide": Toggle(window); break;
                    case "plain":
                    case "markdown":
                        dialogs.Path = transition == "plain" ? "notes.txt" : "notes.md";
                        Assert.True(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
                        Assert.False(window.PreviewScroll.IsVisible);
                        break;
                    case "close": Close(window); break;
                    default: throw new InvalidOperationException("Unknown transition.");
                }
            }
            finally { release.Set(); }
            await WaitForWorkAsync(window).ConfigureAwait(true);
            if (transition is "edit" or "open")
            {
                Assert.Equal(2, calls);
                Assert.Equal(transition == "edit" ? "Latest 19" : "Other document", Assert.IsType<TextBlock>(Assert.Single(window.PreviewBlocks.Children)).Inlines!.Text);
            }
            else
            {
                Assert.Equal(1, calls);
                Assert.Empty(window.PreviewBlocks.Children);
            }
            if (transition != "close") { Close(window); }
        }).ConfigureAwait(true);
    }

    /// <summary>Rapid edits coalesce and a closed pane cancels parsing before it starts.</summary>
    [Fact]
    public async Task DebouncesAndCancelsBeforeParsing()
    {
        await OnUiAsync(async () =>
        {
            int calls = 0;
            EditorWindow window = new(new Store(), new Dialogs { Path = "notes.textile" }, (format, source) =>
            {
                _ = Interlocked.Increment(ref calls);
                return Parse(format, source);
            });
            window.Show();
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            Toggle(window);
            for (int edit = 0; edit < 20; edit++) { window.Editor.Text = $"h1. Latest {edit}"; }
            await WaitForWorkAsync(window).ConfigureAwait(true);
            Assert.Equal(1, calls);
            Assert.Equal("Latest 19", Assert.IsType<TextBlock>(Assert.Single(window.PreviewBlocks.Children)).Inlines!.Text);
            window.Editor.Text = "h1. Pending";
            Close(window);
            await WaitForWorkAsync(window).ConfigureAwait(true);
            Assert.Equal(1, calls);
            Assert.Empty(window.PreviewBlocks.Children);
        }).ConfigureAwait(true);
    }

    /// <summary>An oversized source shows a limit notice while editing and undo remain available.</summary>
    [Fact]
    public async Task SourceLimitPreservesEditing()
    {
        await OnUiAsync(async () =>
        {
            EditorWindow window = new(new Store(), new Dialogs { Path = "notes.textile" });
            window.Show();
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            Toggle(window);
            window.Editor.Document.Insert(window.Editor.Document.TextLength, new string('x', 32_769));
            await WaitForWorkAsync(window).ConfigureAwait(true);
            TextBlock notice = Assert.IsType<TextBlock>(Assert.Single(window.PreviewBlocks.Children));
            Assert.Equal("Preview limit reached. Continue editing in the source pane.", notice.Text);
            Assert.True(window.ViewModel.IsDirty);
            window.Editor.Document.UndoStack.Undo();
            Assert.Equal("h1. Heading", window.Editor.Text);
            Assert.False(window.ViewModel.IsDirty);
            await WaitForWorkAsync(window).ConfigureAwait(true);
            Assert.Equal("Heading", Assert.IsType<TextBlock>(Assert.Single(window.PreviewBlocks.Children)).Inlines!.Text);
            Close(window);
        }).ConfigureAwait(true);
    }

    /// <summary>Invalid UTF-16 stays literal in the source and native preview text.</summary>
    [Fact]
    public async Task InvalidUnicodeRemainsLiteral()
    {
        await OnUiAsync(async () =>
        {
            EditorWindow window = new(new Store(), new Dialogs { Path = "notes.textile" });
            window.Show();
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            const string source = "p. before \ud800 between \udfff after";
            window.Editor.Text = source;
            Toggle(window);
            await WaitForWorkAsync(window).ConfigureAwait(true);
            TextBlock text = Assert.IsType<TextBlock>(Assert.Single(window.PreviewBlocks.Children));
            Assert.Equal("before \ud800 between \udfff after", text.Inlines!.Text);
            Assert.Equal(source, window.Editor.Text);
            Assert.True(window.ViewModel.IsDirty);
            Close(window);
        }).ConfigureAwait(true);
    }

    private static PreviewDocument Parse(DocumentFormat format, string source)
    {
        return format switch
        {
            DocumentFormat.Textile => TextilePreviewParser.Parse(source),
            DocumentFormat.Markdown => MarkdownPreviewParser.Parse(source),
            DocumentFormat.PlainText => throw new InvalidOperationException("Plain text must not enter a preview parser."),
            _ => throw new InvalidOperationException("Plain text must not enter a preview parser."),
        };
    }

    private static async Task OnUiAsync(Func<Task> test)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            await test().ConfigureAwait(true);
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static Task WaitForWorkAsync(EditorWindow window)
    {
        return window.Preview.Work.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    private static void Toggle(EditorWindow window)
    {
        window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void Close(EditorWindow window)
    {
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsVisible);
    }

    private sealed class Store : ITextDocumentStore
    {
        internal string LoadedText { get; set; } = "h1. Heading";
        public Task<StoredDocument> LoadAsync(string destination, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StoredDocument(LoadedText, destination));
        }
        public Task<string?> SaveAsync(string destination, string text, string? expectedRevision, bool overwrite, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(destination);
        }
    }

    private sealed class Dialogs : IEditorDialogs
    {
        internal string? Path { get; set; }
        public Task<ConflictDecision> ConfirmConflictAsync(string destination) { return Task.FromResult(ConflictDecision.Cancel); }
        public Task<string?> ChooseOpenAsync() { return Task.FromResult(Path); }
        public Task<string?> ChooseSaveAsync(string? destination) { return Task.FromResult(Path); }
        public Task<UnsavedDecision> ConfirmUnsavedAsync() { return Task.FromResult(UnsavedDecision.Discard); }
        public Task ShowErrorAsync(string message) { return Task.CompletedTask; }
    }
}

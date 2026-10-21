using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit.Highlighting;
using Editor.App.Services;
using Editor.App.Views;
using Editor.Application;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks format-driven source highlighting through the editor window.</summary>
public sealed class MarkdownHighlightingTests
{
    private static readonly string[] Roles = ["Heading", "Strong", "Emphasis", "Code", "Link", "Quote", "List"];
    private static readonly string[] Destinations = ["notes.markdown", "notes.txt", "notes.md", "notes.unknown"];

    /// <summary>Open selects highlighting from the authoritative session format.</summary>
    /// <param name="path">The selected document destination.</param>
    /// <param name="markdown">Whether Markdown highlighting is expected.</param>
    [Theory]
    [InlineData("notes.md", true)]
    [InlineData("notes.MARKDOWN", true)]
    [InlineData("notes.txt", false)]
    [InlineData("notes.unknown", false)]
    public async Task OpenSelectsHighlighting(string path, bool markdown)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            EditorWindow window = new(new Store(), new Dialogs { Path = path });
            Assert.Null(window.Editor.SyntaxHighlighting);
            IBrush plainLink = window.Editor.TextArea.TextView.LinkTextForegroundBrush;
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            Assert.Equal(markdown, window.Editor.SyntaxHighlighting is not null);
            Assert.Equal("# Heading\r\nordinary text", window.Editor.Text);
            Assert.False(window.ViewModel.IsDirty);
            if (!markdown)
            {
                Assert.Same(plainLink, window.Editor.TextArea.TextView.LinkTextForegroundBrush);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>The native highlighter recognizes the documented baseline and excludes ordinary text.</summary>
    [Fact]
    public async Task HighlightsBaseline()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            EditorWindow window = new(new Store(), new Dialogs { Path = "notes.md" });
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            window.Editor.Text = "# Heading\n**strong**\n*emphasis*\n`code`\n[link](https://example.com)\n> quote\n- item\n1. item\n```cs\n# code\n```\nordinary text\n~~~\n*code*\n~~~\nordinary text\nword_under_score\n#not a heading\n\\*escaped*";
            using DocumentHighlighter highlighter = new(window.Editor.Document, window.Editor.SyntaxHighlighting);
            string[] expected = ["Heading", "Strong", "Emphasis", "Code", "Link", "Quote", "List", "List", "Code", "Code", "Code", "", "Code", "Code", "Code", "", "", "", ""];
            for (int line = 1; line <= expected.Length; line++)
            {
                string[] names = [.. highlighter.HighlightLine(line).Sections.Select(section => section.Color.Name).Distinct()];
                if (expected[line - 1].Length == 0)
                {
                    Assert.Empty(names);
                }
                else
                {
                    Assert.Equal([expected[line - 1]], names);
                }
            }
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Successful saves and theme switches preserve the buffer and its undo history.</summary>
    [Fact]
    public async Task SaveAndThemeTransitionsPreserveEditingState()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            Store store = new();
            Dialogs dialogs = new() { Path = "notes.md" };
            EditorWindow window = new(store, dialogs);
            window.Show();
            IBrush plainLink = window.Editor.TextArea.TextView.LinkTextForegroundBrush;
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            window.Editor.Document.Insert(window.Editor.Document.TextLength, "\n**edit**");
            window.Editor.Select(2, 5);
            int caret = window.Editor.CaretOffset;
            Guid identity = window.ViewModel.DocumentId;
            const string text = "# Heading\r\nordinary text\n**edit**";
            foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = theme;
                Dispatcher.UIThread.RunJobs();
                foreach (string role in Roles)
                {
                    HighlightingColor color = window.Editor.SyntaxHighlighting.GetNamedColor(role);
                    Color foreground = color.Foreground.GetColor(null)!.Value;
                    Assert.Equal(((ISolidColorBrush)window.Editor.FindResource(window.Editor.ActualThemeVariant, $"Brush.Markdown.{role}")!).Color, foreground);
                    Color background = ((ISolidColorBrush)window.Editor.FindResource(window.Editor.ActualThemeVariant, "Brush.Background")!).Color;
                    Assert.True(Contrast(foreground, background) >= 4.5, $"Insufficient contrast for {role} in {theme}.");
                }

                Assert.Equal(window.Editor.SyntaxHighlighting.GetNamedColor("Link").Foreground.GetColor(null),
                    ((ISolidColorBrush)window.Editor.TextArea.TextView.LinkTextForegroundBrush).Color);
                Assert.Equal(text, window.Editor.Text);
                Assert.True(window.ViewModel.IsDirty);
                Assert.Equal(2, window.Editor.SelectionStart);
                Assert.Equal(5, window.Editor.SelectionLength);
                Assert.Equal(caret, window.Editor.CaretOffset);
            }

            foreach (string path in Destinations)
            {
                dialogs.Path = path;
                TaskCompletionSource completion = new();
                store.Save = completion.Task;
                bool wasHighlighted = window.Editor.SyntaxHighlighting is not null;
                Task<bool> save = window.ViewModel.SaveAsAsync();
                Assert.Equal(wasHighlighted, window.Editor.SyntaxHighlighting is not null);
                completion.SetResult();
                Assert.True(await save.ConfigureAwait(true));
                Assert.Equal(path is "notes.markdown" or "notes.md", window.Editor.SyntaxHighlighting is not null);
                Assert.Equal(text, store.SavedText);
                Assert.Equal(text, window.Editor.Text);
                Assert.Equal(identity, window.ViewModel.DocumentId);
                Assert.False(window.ViewModel.IsDirty);
                Assert.Equal(2, window.Editor.SelectionStart);
                Assert.Equal(5, window.Editor.SelectionLength);
                Assert.Equal(caret, window.Editor.CaretOffset);
            }

            window.Editor.Document.UndoStack.Undo();
            Assert.Equal("# Heading\r\nordinary text", window.Editor.Text);
            window.Editor.Document.UndoStack.Redo();
            Assert.Equal(text, window.Editor.Text);
            Assert.True(await window.ViewModel.NewAsync().ConfigureAwait(true));
            Assert.Null(window.Editor.SyntaxHighlighting);
            Assert.Equal(string.Empty, window.Editor.Text);
            Assert.Same(plainLink, window.Editor.TextArea.TextView.LinkTextForegroundBrush);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Failed saves and cancelled operations retain the previous highlighting and dirty buffer.</summary>
    /// <param name="source">The original destination.</param>
    /// <param name="destination">The attempted save destination.</param>
    [Theory]
    [InlineData("notes.md", "notes.txt")]
    [InlineData("notes.txt", "notes.md")]
    public async Task UnsuccessfulOperationsRetainHighlighting(string source, string destination)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            Store store = new();
            Dialogs dialogs = new() { Path = source };
            EditorWindow window = new(store, dialogs);
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            window.Editor.Document.Insert(0, "!");
            IHighlightingDefinition definition = window.Editor.SyntaxHighlighting;
            dialogs.Path = destination;
            store.Save = Task.FromException(new IOException("Save failed."));
            Assert.False(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
            Assert.Same(definition, window.Editor.SyntaxHighlighting);
            Assert.Equal("Save failed.", dialogs.Error);
            TaskCompletionSource completion = new();
            store.Save = completion.Task;
            Task<bool> save = window.ViewModel.SaveAsAsync();
            window.ViewModel.Cancel();
            Assert.False(await save.ConfigureAwait(true));
            Assert.Same(definition, window.Editor.SyntaxHighlighting);
            dialogs.Path = null;
            Assert.False(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
            Assert.False(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            Assert.Same(definition, window.Editor.SyntaxHighlighting);
            Assert.Equal("!# Heading\r\nordinary text", window.Editor.Text);
            Assert.True(window.ViewModel.IsDirty);
            window.Editor.Document.UndoStack.Undo();
            Assert.Equal("# Heading\r\nordinary text", window.Editor.Text);
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static double Contrast(Color first, Color second)
    {
        static double Linear(double value)
        {
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        double alpha = first.A / 255.0;
        double red = ((first.R * alpha) + (second.R * (1 - alpha))) / 255.0;
        double green = ((first.G * alpha) + (second.G * (1 - alpha))) / 255.0;
        double blue = ((first.B * alpha) + (second.B * (1 - alpha))) / 255.0;
        double a = (0.2126 * Linear(red)) + (0.7152 * Linear(green)) + (0.0722 * Linear(blue));
        double b = (0.2126 * Linear(second.R / 255.0)) + (0.7152 * Linear(second.G / 255.0)) + (0.0722 * Linear(second.B / 255.0));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private sealed class Store : ITextDocumentStore
    {
        internal Task Save { get; set; } = Task.CompletedTask;
        internal string? SavedText { get; private set; }
        public async Task<StoredDocument> LoadAsync(string destination, CancellationToken cancellationToken = default)
        {
            return new(await Task.FromResult("# Heading\r\nordinary text").ConfigureAwait(true), destination);
        }

        public async Task<string?> SaveAsync(string destination, string text, string? expectedRevision, bool overwrite, CancellationToken cancellationToken = default)
        {
            SavedText = text;
            await Save.WaitAsync(cancellationToken).ConfigureAwait(true);
            return destination;
        }
    }

    private sealed class Dialogs : IEditorDialogs
    {
        public Task<ConflictDecision> ConfirmConflictAsync(string destination) { return Task.FromResult(ConflictDecision.Cancel); }
        internal string? Path { get; set; }
        internal string? Error { get; private set; }
        public Task<string?> ChooseOpenAsync()
        {
            return Task.FromResult(Path);
        }

        public Task<string?> ChooseSaveAsync(string? destination)
        {
            return Task.FromResult(Path);
        }

        public Task<UnsavedDecision> ConfirmUnsavedAsync()
        {
            return Task.FromResult(UnsavedDecision.Discard);
        }

        public Task ShowErrorAsync(string message) { Error = message; return Task.CompletedTask; }
    }
}

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit.Highlighting;
using Editor.App.Services;
using Editor.App.Views;
using Editor.Application;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks Textile source highlighting through the editor window.</summary>
public sealed class TextileHighlightingTests
{
    private static readonly string[] Roles = ["Heading", "Strong", "Emphasis", "Underline", "Strike", "Code", "Link", "Quote", "List", "Table", "Rule", "Paragraph", "Macro"];
    private static readonly (string Line, string Color)[] Baseline =
    [
        ("h1. Heading", "Heading"),
        ("h2>. Right", "Heading"),
        ("*strong*", "Strong"),
        ("_emphasis_", "Emphasis"),
        ("+underline+", "Underline"),
        ("-strike-", "Strike"),
        ("@code@", "Code"),
        ("<code>inline</code>", "Code"),
        ("\"link\":https://example.com", "Link"),
        ("[[WikiPage|Caption]]", "Link"),
        ("#123", "Link"),
        ("##123", "Link"),
        ("#123#note-6", "Link"),
        ("bq. quote", "Quote"),
        ("* item", "List"),
        ("## nested", "List"),
        ("*# mixed", "List"),
        ("|cell|", "Table"),
        ("----", "Rule"),
        ("p. paragraph", "Paragraph"),
        ("!picture.png!", "Link"),
        ("{{toc}}", "Macro"),
        ("{{include(Wiki)}}", "Macro"),
        ("ordinary text", ""),
        ("well-known", ""),
        ("h1not", ""),
        ("*not", ""),
        ("r123", ""),
        ("C++", ""),
        ("attachment:file.png", ""),
        ("<script>alert(1)</script>", ""),
        ("<notextile>*strong*</notextile>", ""),
        ("==*strong*==", ""),
        ("<pre>", "Code"),
        ("h1. literal", "Code"),
        ("</pre>", "Code"),
        ("bc. code", "Code"),
        ("still code", "Code"),
        ("", ""),
        ("pre. sample", "Code"),
        ("", ""),
        ("after code", ""),
    ];

    /// <summary>Open selects Textile highlighting only for a .textile name.</summary>
    /// <param name="path">The selected document destination.</param>
    /// <param name="definition">The expected highlighting definition name, or null.</param>
    [Theory]
    [InlineData("notes.textile", "Textile")]
    [InlineData("notes.TEXTILE", "Textile")]
    [InlineData("notes.wiki", null)]
    [InlineData("notes.md", "Markdown")]
    [InlineData("notes.txt", null)]
    public async Task OpenSelectsHighlighting(string path, string? definition)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            EditorWindow window = new(new Store(), new Dialogs { Path = path });
            IBrush plainLink = window.Editor.TextArea.TextView.LinkTextForegroundBrush;
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            Assert.Equal(definition, window.Editor.SyntaxHighlighting?.Name);
            Assert.Equal(definition == "Markdown", window.PreviewToggle.IsVisible);
            Assert.False(window.PreviewScroll.IsVisible);
            if (definition is null)
            {
                Assert.Same(plainLink, window.Editor.TextArea.TextView.LinkTextForegroundBrush);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>The native highlighter recognizes the documented Textile baseline.</summary>
    [Fact]
    public async Task HighlightsBaseline()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            EditorWindow window = new(new Store(), new Dialogs { Path = "notes.textile" });
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            window.Editor.Text = string.Join('\n', Baseline.Select(line => line.Line));
            using DocumentHighlighter highlighter = new(window.Editor.Document, window.Editor.SyntaxHighlighting);
            for (int line = 1; line <= Baseline.Length; line++)
            {
                string[] names = [.. highlighter.HighlightLine(line).Sections.Select(section => section.Color.Name).Where(name => !string.IsNullOrEmpty(name)).Distinct()];
                string expected = Baseline[line - 1].Color;
                string[] expectedNames = expected.Length == 0 ? [] : [expected];
                Assert.True(names.SequenceEqual(expectedNames), $"Line {line} '{Baseline[line - 1].Line}' has [{string.Join(", ", names)}] and expected [{expected}].");
            }

            HighlightingColor strike = window.Editor.SyntaxHighlighting.GetNamedColor("Strike");
            HighlightingColor underline = window.Editor.SyntaxHighlighting.GetNamedColor("Underline");
            Assert.Equal(true, strike.Strikethrough);
            Assert.Equal(true, underline.Underline);
            Assert.Equal(FontWeight.Bold, window.Editor.SyntaxHighlighting.GetNamedColor("Heading").FontWeight);
            Assert.Equal(FontStyle.Italic, window.Editor.SyntaxHighlighting.GetNamedColor("Emphasis").FontStyle);
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Theme changes keep Textile colors, text, and undo history.</summary>
    [Fact]
    public async Task ThemeChangesPreserveEditingState()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            EditorWindow window = new(new Store(), new Dialogs { Path = "notes.textile" });
            window.Show();
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            window.Editor.Document.Insert(window.Editor.Document.TextLength, "\nh1. Edit");
            window.Editor.Select(2, 5);
            int caret = window.Editor.CaretOffset;
            const string text = "# Heading\r\nordinary text\nh1. Edit";
            foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = theme;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("Textile", window.Editor.SyntaxHighlighting.Name);
                foreach (string role in Roles)
                {
                    HighlightingColor color = window.Editor.SyntaxHighlighting.GetNamedColor(role);
                    Color foreground = color.Foreground.GetColor(null)!.Value;
                    Assert.Equal(((ISolidColorBrush)window.Editor.FindResource(window.Editor.ActualThemeVariant, $"Brush.Textile.{role}")!).Color, foreground);
                    Color background = ((ISolidColorBrush)window.Editor.FindResource(window.Editor.ActualThemeVariant, "Brush.Background")!).Color;
                    Assert.True(Contrast(foreground, background) >= 4.5, $"Insufficient contrast for {role} in {theme}.");
                }

                Assert.Equal(text, window.Editor.Text);
                Assert.Equal(2, window.Editor.SelectionStart);
                Assert.Equal(5, window.Editor.SelectionLength);
                Assert.Equal(caret, window.Editor.CaretOffset);
            }

            window.Editor.Document.UndoStack.Undo();
            Assert.Equal("# Heading\r\nordinary text", window.Editor.Text);
            window.Editor.Document.UndoStack.Redo();
            Assert.Equal(text, window.Editor.Text);
            window.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Saving a Markdown preview as Textile closes the preview and keeps the source.</summary>
    [Fact]
    public async Task TextileSaveClosesMarkdownPreview()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            Store store = new();
            Dialogs dialogs = new() { Path = "notes.md" };
            EditorWindow window = new(store, dialogs);
            window.Show();
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.Preview.Work.ConfigureAwait(true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.PreviewScroll.IsVisible);
            Assert.NotEmpty(window.PreviewBlocks.Children);
            window.Editor.Document.Insert(window.Editor.Document.TextLength, "\nh1. Title");
            const string text = "# Heading\r\nordinary text\nh1. Title";
            dialogs.Path = "notes.textile";
            Assert.True(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
            Assert.Equal("Textile", window.Editor.SyntaxHighlighting.Name);
            Assert.False(window.PreviewToggle.IsVisible);
            Assert.False(window.PreviewScroll.IsVisible);
            Assert.Empty(window.PreviewBlocks.Children);
            Assert.Equal(text, store.SavedText);
            Assert.Equal(text, window.Editor.Text);
            Assert.False(window.ViewModel.IsDirty);
            window.Editor.Document.UndoStack.Undo();
            Assert.Equal("# Heading\r\nordinary text", window.Editor.Text);
            dialogs.Path = "notes.wiki";
            Assert.True(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
            Assert.Null(window.Editor.SyntaxHighlighting);
            Assert.False(window.PreviewToggle.IsVisible);
            Assert.Equal("# Heading\r\nordinary text", window.Editor.Text);
            window.Close();
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
        internal string? SavedText { get; private set; }

        public async Task<StoredDocument> LoadAsync(string destination, CancellationToken cancellationToken = default)
        {
            return new(await Task.FromResult("# Heading\r\nordinary text").ConfigureAwait(true), destination);
        }

        public Task<string?> SaveAsync(string destination, string text, string? expectedRevision, bool overwrite, CancellationToken cancellationToken = default)
        {
            SavedText = text;
            return Task.FromResult<string?>(destination);
        }
    }

    private sealed class Dialogs : IEditorDialogs
    {
        public Task<ConflictDecision> ConfirmConflictAsync(string destination)
        {
            return Task.FromResult(ConflictDecision.Cancel);
        }

        internal string? Path { get; set; }

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

        public Task ShowErrorAsync(string message)
        {
            return Task.CompletedTask;
        }
    }
}

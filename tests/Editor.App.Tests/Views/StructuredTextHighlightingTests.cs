using System.Diagnostics;
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

/// <summary>Checks visual structured-text highlighting through the editor window.</summary>
public sealed class StructuredTextHighlightingTests(ITestOutputHelper output)
{
    /// <summary>Checks source tokens, theme colors, and unchanged editing state.</summary>
    /// <param name="path">The opened destination.</param>
    /// <param name="definition">The native syntax definition name.</param>
    [Theory]
    [InlineData("data.JSON", "Json")]
    [InlineData("data.XML", "XML")]
    [InlineData("view.XAML", "XML")]
    public async Task HighlightingPreservesSourceAndUsesTheme(string path, string definition)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            Store store = new();
            EditorWindow window = new(store, new Dialogs(path));
            window.Show();
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            Assert.Equal(definition, window.Editor.SyntaxHighlighting.Name);
            Assert.False(window.PreviewToggle.IsVisible);
            Assert.False(window.ViewModel.IsDirty);
            Assert.Equal(Store.Text, window.Editor.Text);
            Guid identity = window.ViewModel.DocumentId;
            window.Editor.Document.Insert(window.Editor.Document.TextLength, "!");
            window.Editor.Select(2, 3);
            int caret = window.Editor.CaretOffset;
            foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = theme;
                Dispatcher.UIThread.RunJobs();
                foreach (HighlightingColor color in window.Editor.SyntaxHighlighting.NamedHighlightingColors)
                {
                    Color foreground = color.Foreground.GetColor(null)!.Value;
                    Assert.Equal(((ISolidColorBrush)window.Editor.FindResource(window.Editor.ActualThemeVariant,
                        $"Brush.{definition}.{color.Name}")!).Color, foreground);
                    Color background = ((ISolidColorBrush)window.Editor.FindResource(window.Editor.ActualThemeVariant, "Brush.Background")!).Color;
                    Assert.True(Contrast(foreground, background) >= 4.5, $"Insufficient contrast for {definition}.{color.Name} in {theme}.");
                }

                Assert.Equal(Store.Text + "!", window.Editor.Text);
                Assert.True(window.ViewModel.IsDirty);
                Assert.Equal(identity, window.ViewModel.DocumentId);
                Assert.Equal(2, window.Editor.SelectionStart);
                Assert.Equal(3, window.Editor.SelectionLength);
                Assert.Equal(caret, window.Editor.CaretOffset);
            }

            Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
            Assert.Equal(Store.Text + "!", store.SavedText);
            window.Editor.Document.UndoStack.Undo();
            Assert.Equal(Store.Text, window.Editor.Text);
            window.Editor.Document.UndoStack.Redo();
            Assert.Equal(Store.Text + "!", window.Editor.Text);
            Assert.True(await window.ViewModel.NewAsync().ConfigureAwait(true));
            Assert.Null(window.Editor.SyntaxHighlighting);
            window.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Checks escaped JSON strings, primitives, multiline nesting, and malformed input.</summary>
    [Fact]
    public async Task JsonRecognizesSourceTokens()
    {
        await CheckTokens("data.json", """
            {
              "escaped": "a\"b\\c", "flag": true, "empty": null,
              "values": [-1.25e+2, false]
            }
            "unterminated
            false
            """, [(2, "escaped", "FieldName"), (2, "flag", "FieldName"), (2, "empty", "FieldName"), (2, "b", "String"), (2, "true", "Bool"),
                (2, "null", "Null"), (3, "1.25e+2", "Number"), (3, "false", "Bool"), (6, "false", "Bool")]).ConfigureAwait(true);
    }

    /// <summary>Checks nested arrays and objects across lines and values after commas.</summary>
    [Fact]
    public async Task JsonRecognizesNestedMultilineValues()
    {
        await CheckTokens("nested.json", /*lang=json,strict*/ """
            {
              "items": [
                1, true, null,
                {"nested": [false, {"name": "a,b}"}], "last": true}
              ],
              "after": false
            }
            """, [(2, "items", "FieldName"), (3, "true", "Bool"), (3, "null", "Null"),
                (4, "nested", "FieldName"), (4, "false", "Bool"), (4, "name", "FieldName"),
                (4, "a,b}", "String"), (4, "last", "FieldName"), (4, "true", "Bool"),
                (6, "after", "FieldName"), (6, "false", "Bool")]).ConfigureAwait(true);
    }

    /// <summary>Checks XML attributes, entities, multiline comments, CDATA, and malformed input.</summary>
    [Fact]
    public async Task XmlRecognizesSourceTokens()
    {
        await CheckTokens("view.xaml", """
            <?xml version="1.0"?>
            <Panel xmlns="urn:test" title='a &amp; b'>
            <!-- comment
            <ignored /> -->
            <![CDATA[<literal>
            &raw;]]>
            <Text value="{Binding Name}" />
            &amp;
            <broken value="unfinished
            <next />
            """, [(1, "xml", "XmlDeclaration"), (2, "Panel", "XmlTag"), (2, "title", "AttributeName"),
                (2, "a ", "AttributeValue"), (2, "&amp;", "Entity"), (4, "ignored", "Comment"),
                (5, "literal", "CData"), (6, "raw", "CData"), (7, "Binding", "AttributeValue"),
                (8, "&amp;", "Entity"), (10, "next", "XmlTag")]).ConfigureAwait(true);
    }

    /// <summary>Exercises full-document highlighting for a large buffer without changing its text.</summary>
    /// <param name="path">The opened destination.</param>
    /// <param name="line">The repeated source line.</param>
    /// <param name="role">The required token role on the final line.</param>
    [Theory]
    [InlineData("large.json", /*lang=json,strict*/ "{\"text\": \"λ café 🐈\", \"value\": true, \"number\": -1.25e+2}\n", "Bool")]
    [InlineData("large.xml", "<Text title=\"λ café 🐈 &amp; text\">content</Text>\n", "XmlTag")]
    public async Task LargeBufferRetainsSource(string path, string line, string role)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            EditorWindow window = new(new Store(), new Dialogs(path));
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            string source = string.Concat(Enumerable.Repeat(line, 20000));
            window.Editor.Text = source;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            Stopwatch elapsed = Stopwatch.StartNew();
            using DocumentHighlighter highlighter = new(window.Editor.Document, window.Editor.SyntaxHighlighting);
            for (int number = 1; number <= 20000; number++)
            {
                Assert.Contains(highlighter.HighlightLine(number).Sections, section => section.Color.Name == role);
            }
            elapsed.Stop();
            output.WriteLine($"{path}: {source.Length} UTF-16 code units, {elapsed.ElapsedMilliseconds} ms, {GC.GetAllocatedBytesForCurrentThread() - allocated} allocated bytes.");
            Assert.Equal(source, window.Editor.Text);
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static async Task CheckTokens(string path, string source, (int Line, string Text, string Role)[] expected)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            EditorWindow window = new(new Store(), new Dialogs(path));
            Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
            window.Editor.Text = source;
            using DocumentHighlighter highlighter = new(window.Editor.Document, window.Editor.SyntaxHighlighting);
            foreach ((int line, string text, string role) in expected)
            {
                AvaloniaEdit.Document.DocumentLine documentLine = window.Editor.Document.GetLineByNumber(line);
                int offset = documentLine.Offset + window.Editor.Document.GetText(documentLine).IndexOf(text, StringComparison.Ordinal);
                Assert.Contains(highlighter.HighlightLine(line).Sections, section =>
                    section.Offset <= offset && section.Offset + section.Length >= offset + text.Length && section.Color.Name == role);
            }
            Assert.Equal(source, window.Editor.Text);
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static double Contrast(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte channel)
            {
                double value = channel / 255.0;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }
            return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));
        }
        double a = Luminance(first);
        double b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private sealed class Store : ITextDocumentStore
    {
        internal const string Text = "λ café 🐈\r\n{\"value\": [true, null, 1.2e-3]}\n<Root title='a &amp; b' />\r";
        internal string? SavedText { get; private set; }
        public Task<StoredDocument> LoadAsync(string destination, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StoredDocument(Text, destination));
        }
        public Task<string?> SaveAsync(string destination, string text, string? expectedRevision, bool overwrite, CancellationToken cancellationToken = default)
        {
            SavedText = text;
            return Task.FromResult<string?>(destination);
        }
    }

    private sealed class Dialogs(string path) : IEditorDialogs
    {
        public Task<string?> ChooseOpenAsync() { return Task.FromResult<string?>(path); }
        public Task<string?> ChooseSaveAsync(string? destination) { return Task.FromResult<string?>(path); }
        public Task<UnsavedDecision> ConfirmUnsavedAsync() { return Task.FromResult(UnsavedDecision.Discard); }
        public Task<ConflictDecision> ConfirmConflictAsync(string destination) { return Task.FromResult(ConflictDecision.Cancel); }
        public Task ShowErrorAsync(string message) { return Task.CompletedTask; }
    }
}

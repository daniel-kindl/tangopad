using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit.Editing;
using Editor.App.Services;
using Editor.App.Views;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks line-number presentation through the editor controls.</summary>
public sealed class LineNumbersTests
{
    /// <summary>The checkbox controls the gutter through keyboard input at the minimum width.</summary>
    /// <param name="dark">Whether the window starts in the dark theme.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LineNumbersToggleControlsGutter(bool dark)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs())
            {
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Width = window.MinWidth;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            CheckBox toggle = Assert.IsType<CheckBox>(window.FindControl<CheckBox>("LineNumbersToggle"));
            Assert.Equal("Line numbers", toggle.Content);
            Assert.True(toggle.IsChecked);
            Assert.Equal(window.Foreground, toggle.Foreground);
            _ = Assert.IsType<ISolidColorBrush>(window.Editor.LineNumbersForeground, exactMatch: false);
            Assert.True(window.Editor.ShowLineNumbers);
            _ = Assert.Single(window.Editor.TextArea.LeftMargins.OfType<LineNumberMargin>());
            Assert.True(toggle.TranslatePoint(default, window)!.Value.X >= 0);
            Assert.True(toggle.TranslatePoint(new(toggle.Bounds.Width, toggle.Bounds.Height), window)!.Value.X <= window.ClientSize.Width);
            _ = toggle.Focus();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.False(toggle.IsChecked);
            Assert.False(window.Editor.ShowLineNumbers);
            Assert.Empty(window.Editor.TextArea.LeftMargins.OfType<LineNumberMargin>());
            Assert.True(toggle.IsFocused);
            window.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark;
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(toggle.IsChecked);
            Assert.True(window.Editor.ShowLineNumbers);
            _ = Assert.Single(window.Editor.TextArea.LeftMargins.OfType<LineNumberMargin>());
            window.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Hiding numbers preserves editing, logical lines, preview, and exact saved bytes.</summary>
    [Fact]
    public async Task LineNumbersPreserveEditingAndDocumentTransitions()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            _ = Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "source.md");
                string saved = Path.Combine(directory, "saved.md");
                string original = "# Heading\r\n" + new string('x', 600) + "\nλ😀\rlast\n";
                await File.WriteAllTextAsync(path, original).ConfigureAwait(true);
                EditorWindow window = new(new FileTextDocumentStore(), new Dialogs { OpenPath = path, SavePath = saved });
                window.Show();
                Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                Guid identity = window.ViewModel.DocumentId;
                window.Editor.Document.Insert(0, "!");
                window.Editor.Document.Insert(window.Editor.Document.TextLength, "?");
                window.Editor.Document.UndoStack.Undo();
                window.Editor.Select(3, 4);
                int caret = window.Editor.CaretOffset;
                window.LineNumbersToggle.IsChecked = false;
                Dispatcher.UIThread.RunJobs();
                Assert.False(window.ViewModel.ShowLineNumbers);
                Assert.False(window.Editor.ShowLineNumbers);
                Assert.Equal(identity, window.ViewModel.DocumentId);
                Assert.Equal("!" + original, window.Editor.Text);
                Assert.Equal(3, window.Editor.SelectionStart);
                Assert.Equal(4, window.Editor.SelectionLength);
                Assert.Equal(caret, window.Editor.CaretOffset);
                Assert.True(window.ViewModel.IsDirty);
                Assert.True(window.Editor.Document.UndoStack.CanUndo);
                Assert.True(window.Editor.Document.UndoStack.CanRedo);
                window.Editor.Document.UndoStack.Redo();
                Assert.Equal("!" + original + "?", window.Editor.Text);
                window.Editor.Document.UndoStack.Undo();
                window.Editor.Document.UndoStack.Undo();
                Assert.Equal(original, window.Editor.Text);
                Assert.False(window.ViewModel.IsDirty);
                window.ViewModel.WordWrap = true;
                window.Editor.CaretOffset = window.Editor.Document.GetOffset(3, 2);
                window.Editor.TextArea.Caret.BringCaretToView();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(5, window.Editor.Document.LineCount);
                Assert.Equal(3, window.ViewModel.CaretLine);
                Assert.Equal(2, window.ViewModel.CaretColumn);
                Assert.True(window.Editor.TextArea.TextView.GetVisualLine(2).TextLines.Count > 1);
                Assert.NotNull(window.Editor.SyntaxHighlighting);
                window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Preview.Work.ConfigureAwait(true);
                Assert.True(window.PreviewScroll.IsVisible);
                window.ViewModel.Search.ShowReplaceCommand.Execute(null);
                window.ViewModel.Search.Query = "Heading";
                window.ViewModel.Search.Replacement = "Title";
                window.ViewModel.Search.ReplaceAllCommand.Execute(null);
                string edited = "# Title\r\n" + new string('x', 600) + "\nλ😀\rlast\n";
                Assert.Equal(edited, window.Editor.Text);
                Assert.True(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
                Assert.Equal(Encoding.UTF8.GetBytes(edited), await File.ReadAllBytesAsync(saved).ConfigureAwait(true));
                Assert.False(window.ViewModel.IsDirty);
                Assert.False(window.Editor.ShowLineNumbers);
                Assert.True(await window.ViewModel.ReloadAsync().ConfigureAwait(true));
                Assert.False(window.Editor.ShowLineNumbers);
                Assert.True(window.PreviewScroll.IsVisible);
                Assert.Equal(edited, window.Editor.Text);
                Assert.True(await window.ViewModel.NewAsync().ConfigureAwait(true));
                Assert.False(window.Editor.ShowLineNumbers);
                Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                Assert.False(window.Editor.ShowLineNumbers);
                Assert.Equal(original, window.Editor.Text);
                window.LineNumbersToggle.IsChecked = true;
                Assert.True(window.Editor.ShowLineNumbers);
                Assert.False(window.ViewModel.IsDirty);
                window.Close();
            }
            finally
            {
                Directory.Delete(directory, true);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private sealed class Dialogs : IEditorDialogs
    {
        internal string? OpenPath { get; init; }
        internal string? SavePath { get; init; }
        public Task<string?> ChooseOpenAsync() { return Task.FromResult(OpenPath); }
        public Task<string?> ChooseSaveAsync(string? destination) { return Task.FromResult(SavePath); }
        public Task<UnsavedDecision> ConfirmUnsavedAsync() { return Task.FromResult(UnsavedDecision.Discard); }
        public Task ShowErrorAsync(string message) { Assert.Fail(message); return Task.CompletedTask; }
    }
}

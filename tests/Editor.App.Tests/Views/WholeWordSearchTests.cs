using System.Text;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Editor.App.Services;
using Editor.App.Views;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks whole-word search through compiled bindings and the editor buffer.</summary>
public sealed class WholeWordSearchTests
{
    /// <summary>The option filters navigation and replacements without moving the caret or changing undo history.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToggleFiltersNavigationAndGroupedReplacement(bool dark)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs())
            {
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            const string original = "cat_cat Cat cat\r\ncat’s 😀cat😀";
            window.Editor.Text = original;
            window.Editor.Document.UndoStack.ClearAll();
            window.ViewModel.Search.ShowFindCommand.Execute(null);
            window.QueryInput.Text = "cat";
            window.Editor.Select(0, 3);
            int caret = window.Editor.CaretOffset;
            CheckBox wholeWord = Assert.IsType<CheckBox>(window.FindControl<CheckBox>("MatchWholeWordInput"));
            Assert.False(wholeWord.IsChecked);
            Assert.Equal("Match whole word", Assert.IsType<AutomationPeer>(ControlAutomationPeer.CreatePeerForElement(wholeWord), exactMatch: false).GetName());
            Assert.Equal(6, window.ViewModel.Search.MatchCount);
            _ = wholeWord.Focus();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.True(wholeWord.IsChecked);
            Assert.Equal(4, window.ViewModel.Search.MatchCount);
            Assert.Equal(0, window.Editor.SelectionStart);
            Assert.Equal(3, window.Editor.SelectionLength);
            Assert.Equal(caret, window.Editor.CaretOffset);
            Assert.False(window.Editor.Document.UndoStack.CanUndo);
            window.ViewModel.Search.NextCommand.Execute(null);
            Assert.Equal(8, window.Editor.SelectionStart);
            window.ViewModel.Search.PreviousCommand.Execute(null);
            Assert.Equal(25, window.Editor.SelectionStart);
            window.ViewModel.Search.NextCommand.Execute(null);
            Assert.Equal(8, window.Editor.SelectionStart);
            window.MatchCaseInput.IsChecked = true;
            Assert.Equal(3, window.ViewModel.Search.MatchCount);
            window.ViewModel.Search.ShowReplaceCommand.Execute(null);
            Assert.True(wholeWord.IsChecked);
            Assert.False(window.ReplaceCurrentButton.IsEffectivelyEnabled);
            window.ViewModel.Search.NextCommand.Execute(null);
            Assert.Equal(12, window.Editor.SelectionStart);
            window.ReplacementInput.Text = "dog";
            window.ViewModel.Search.ReplaceCurrentCommand.Execute(null);
            Assert.Equal("cat_cat Cat dog\r\ncat’s 😀cat😀", window.Editor.Text);
            Assert.Equal("dog", window.Editor.SelectedText);
            Assert.Equal(1, window.ViewModel.Search.ReplacementCount);
            window.Editor.Document.UndoStack.Undo();
            window.ViewModel.Search.ReplaceAllCommand.Execute(null);
            const string replaced = "cat_cat Cat dog\r\ndog’s 😀dog😀";
            Assert.Equal(replaced, window.Editor.Text);
            Assert.Equal(3, window.ViewModel.Search.ReplacementCount);
            window.Editor.Document.UndoStack.Undo();
            Assert.Equal(original, window.Editor.Text);
            Assert.False(window.Editor.Document.UndoStack.CanUndo);
            window.Editor.Document.UndoStack.Redo();
            Assert.Equal(replaced, window.Editor.Text);
            wholeWord.IsChecked = false;
            Assert.Equal(0, window.ViewModel.Search.ReplacementCount);
            Assert.Equal(2, window.ViewModel.Search.MatchCount);
            window.ViewModel.Search.CloseCommand.Execute(null);
            window.ViewModel.Search.ShowFindCommand.Execute(null);
            Assert.False(wholeWord.IsChecked);
            window.ViewModel.Text = string.Empty;
            window.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Open, Reload, and New preserve window search options and exact Markdown bytes.</summary>
    [Fact]
    public async Task DocumentTransitionsKeepOptionsLocalAndPreserveSource()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.md");
            const string original = "# cat cat_cat\r\ncat’s 😀cat😀\n";
            try
            {
                await File.WriteAllTextAsync(path, original).ConfigureAwait(true);
                EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
                window.Show();
                window.ViewModel.Search.ShowReplaceCommand.Execute(null);
                window.QueryInput.Text = "cat";
                CheckBox wholeWord = Assert.IsType<CheckBox>(window.FindControl<CheckBox>("MatchWholeWordInput"));
                wholeWord.IsChecked = true;
                Assert.True(await window.ViewModel.OpenPathAsync(path).ConfigureAwait(true));
                Assert.True(wholeWord.IsChecked);
                Assert.Equal(3, window.ViewModel.Search.MatchCount);
                Assert.NotNull(window.Editor.SyntaxHighlighting);
                window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Preview.Work.ConfigureAwait(true);
                TextBlock heading = Assert.IsType<TextBlock>(window.PreviewBlocks.Children[0]);
                Assert.Equal("cat cat_cat", heading.Inlines!.Text);
                wholeWord.IsChecked = false;
                wholeWord.IsChecked = true;
                Assert.Same(heading, window.PreviewBlocks.Children[0]);
                Assert.Equal(original, window.Editor.Text);
                Assert.False(window.ViewModel.IsDirty);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                window.ReplacementInput.Text = "dog";
                window.ViewModel.Search.ReplaceAllCommand.Execute(null);
                Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                Assert.Equal(Encoding.UTF8.GetBytes("# dog cat_cat\r\ndog’s 😀dog😀\n"), await File.ReadAllBytesAsync(path).ConfigureAwait(true));
                await File.WriteAllTextAsync(path, original).ConfigureAwait(true);
                Assert.True(await window.ViewModel.ReloadAsync().ConfigureAwait(true));
                Assert.Equal(original, window.Editor.Text);
                Assert.True(wholeWord.IsChecked);
                Assert.Equal(3, window.ViewModel.Search.MatchCount);
                Assert.Equal(0, window.Editor.SelectionLength);
                Assert.Equal(0, window.Editor.CaretOffset);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.True(await window.ViewModel.NewAsync().ConfigureAwait(true));
                Assert.True(wholeWord.IsChecked);
                Assert.Equal("cat", window.ViewModel.Search.Query);
                Assert.Equal(0, window.ViewModel.Search.MatchCount);
                EditorWindow other = new(new FileTextDocumentStore(), new Dialogs());
                other.Show();
                Assert.False(Assert.IsType<CheckBox>(other.FindControl<CheckBox>("MatchWholeWordInput")).IsChecked);
                other.Close();
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
        public Task<string?> ChooseOpenAsync() { return Task.FromResult<string?>(null); }
        public Task<string?> ChooseSaveAsync(string? destination) { return Task.FromResult(destination); }
        public Task<UnsavedDecision> ConfirmUnsavedAsync() { return Task.FromResult(UnsavedDecision.Discard); }
        public Task<ConflictDecision> ConfirmConflictAsync(string destination) { return Task.FromResult(ConflictDecision.Overwrite); }
        public Task ShowErrorAsync(string message) { Assert.Fail(message); return Task.CompletedTask; }
    }
}

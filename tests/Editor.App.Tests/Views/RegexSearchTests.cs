using System.Text;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Editor.App.Services;
using Editor.App.ViewModels;
using Editor.App.Views;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks regex search through the production controls.</summary>
public sealed class RegexSearchTests
{
    /// <summary>The search panel exposes an accessible regex option that starts off.</summary>
    [Fact]
    public async Task RegexOptionIsAccessibleAndDefaultsOff()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
            window.Show();
            try
            {
                window.ViewModel.Search.ShowFindCommand.Execute(null);
                CheckBox toggle = Assert.IsType<CheckBox>(window.FindControl<CheckBox>("UseRegexInput"));
                Assert.False(toggle.IsChecked);
                TextBlock hint = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("RegexReplacementHint"));
                Assert.False(hint.IsEffectivelyVisible);
                toggle.IsChecked = true;
                Assert.False(hint.IsEffectivelyVisible);
                window.ViewModel.Search.ShowReplaceCommand.Execute(null);
                Assert.True(hint.IsEffectivelyVisible);
                window.ViewModel.Search.ShowFindCommand.Execute(null);
                Assert.False(hint.IsEffectivelyVisible);
                Assert.Equal("Use regular expressions", Assert.IsType<AutomationPeer>(ControlAutomationPeer.CreatePeerForElement(toggle), exactMatch: false).GetName());
            }
            finally
            {
                window.Close();
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Keyboard toggling, navigation, capture replacements, and grouped undo work in both themes.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegexControlsPreserveFocusSelectionAndUndo(bool dark)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs())
            {
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                const string original = "cat12 cat_cat3 CAT4\r\n😀cat5😀";
                window.Editor.Text = original;
                window.Editor.Document.UndoStack.ClearAll();
                window.ViewModel.Search.ShowReplaceCommand.Execute(null);
                window.QueryInput.Text = @"cat(\d+)";
                window.ReplacementInput.Text = "dog$1!";
                window.FindControl<CheckBox>("MatchWholeWordInput")!.IsChecked = true;
                window.Editor.Select(1, 2);
                int caret = window.Editor.CaretOffset;
                CheckBox toggle = Assert.IsType<CheckBox>(window.FindControl<CheckBox>("UseRegexInput"));
                _ = toggle.Focus();
                window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
                Assert.True(toggle.IsChecked);
                await window.ViewModel.Search.Work.ConfigureAwait(true);
                Assert.True(toggle.IsFocused);
                Assert.Equal(caret, window.Editor.CaretOffset);
                Assert.Equal(1, window.Editor.SelectionStart);
                Assert.Equal(2, window.Editor.SelectionLength);
                Assert.Equal(3, window.ViewModel.Search.MatchCount);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                window.ViewModel.Search.NextCommand.Execute(null);
                Assert.Equal("CAT4", window.Editor.SelectedText);
                window.ViewModel.Search.PreviousCommand.Execute(null);
                Assert.Equal("cat12", window.Editor.SelectedText);
                window.ViewModel.Search.PreviousCommand.Execute(null);
                Assert.Equal("cat5", window.Editor.SelectedText);
                window.ViewModel.Search.NextCommand.Execute(null);
                Assert.Equal("cat12", window.Editor.SelectedText);
                window.ViewModel.Search.ReplaceCurrentCommand.Execute(null);
                Assert.Equal("dog12! cat_cat3 CAT4\r\n😀cat5😀", window.Editor.Text);
                Assert.Equal("dog12!", window.Editor.SelectedText);
                Assert.Equal(1, window.ViewModel.Search.ReplacementCount);
                await window.ViewModel.Search.Work.ConfigureAwait(true);
                window.Editor.Document.UndoStack.Undo();
                await window.ViewModel.Search.Work.ConfigureAwait(true);
                Assert.Equal(original, window.Editor.Text);
                window.ViewModel.Search.ReplaceAllCommand.Execute(null);
                const string replaced = "dog12! cat_cat3 dog4!\r\n😀dog5!😀";
                Assert.Equal(replaced, window.Editor.Text);
                Assert.Equal(3, window.ViewModel.Search.ReplacementCount);
                await window.ViewModel.Search.Work.ConfigureAwait(true);
                Assert.True(window.ViewModel.IsDirty);
                window.Editor.Document.UndoStack.Undo();
                await window.ViewModel.Search.Work.ConfigureAwait(true);
                Assert.Equal(original, window.Editor.Text);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                window.Editor.Document.UndoStack.Redo();
                await window.ViewModel.Search.Work.ConfigureAwait(true);
                Assert.Equal(replaced, window.Editor.Text);
                window.ViewModel.Search.CloseCommand.Execute(null);
                Assert.True(window.Editor.TextArea.IsFocused);
                window.ViewModel.Search.ShowFindCommand.Execute(null);
                Assert.True(toggle.IsChecked);
            }
            finally
            {
                window.ViewModel.Text = string.Empty;
                window.Close();
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Errors stay in the panel, preserve text, and allow recovery without a modal dialog.</summary>
    [Fact]
    public async Task ErrorFeedbackAndCancellationRemainResponsive()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
            window.Show();
            try
            {
                window.Editor.Text = new string('a', 30000) + "!";
                window.Editor.Document.UndoStack.ClearAll();
                SearchViewModel search = window.ViewModel.Search;
                search.ShowReplaceCommand.Execute(null);
                search.UseRegex = true;
                window.QueryInput.Text = "(a+)+$";
                Assert.True(search.IsSearching);
                Assert.True(Assert.IsType<TextBlock>(window.FindControl<TextBlock>("SearchingText")).IsVisible);
                TaskCompletionSource responsive = new();
                Dispatcher.UIThread.Post(() => responsive.SetResult(), DispatcherPriority.Send);
                await responsive.Task.ConfigureAwait(true);
                await search.Work.ConfigureAwait(true);
                Assert.True(Assert.IsType<TextBlock>(window.FindControl<TextBlock>("SearchTimedOutText")).IsVisible);
                Assert.False(window.ReplaceAllButton.IsEffectivelyEnabled);
                window.QueryInput.Text = "(";
                await search.Work.ConfigureAwait(true);
                TextBlock invalid = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("InvalidPatternText"));
                Assert.True(invalid.IsVisible);
                Assert.Equal("Invalid regular expression. Check the pattern.", invalid.Text);
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Assert.IsType<WrapPanel>(invalid.Parent)));
                window.QueryInput.Text = "(?=!)";
                await search.Work.ConfigureAwait(true);
                Assert.True(Assert.IsType<TextBlock>(window.FindControl<TextBlock>("EmptyRegexMatchText")).IsVisible);
                window.QueryInput.Text = "(!)";
                window.ReplacementInput.Text = "$2";
                await search.Work.ConfigureAwait(true);
                Assert.True(Assert.IsType<TextBlock>(window.FindControl<TextBlock>("InvalidReplacementText")).IsVisible);
                Assert.True(window.NextMatchButton.IsEffectivelyEnabled);
                Assert.False(window.ReplaceAllButton.IsEffectivelyEnabled);
                search.ShowFindCommand.Execute(null);
                await search.Work.ConfigureAwait(true);
                Assert.False(search.HasSearchError);
                Assert.False(Assert.IsType<TextBlock>(window.FindControl<TextBlock>("InvalidReplacementText")).IsVisible);
                search.ShowReplaceCommand.Execute(null);
                await search.Work.ConfigureAwait(true);
                Assert.True(search.InvalidReplacement);
                window.ReplacementInput.Text = "$1";
                await search.Work.ConfigureAwait(true);
                Assert.True(window.ReplaceAllButton.IsEffectivelyEnabled);
                search.CanEdit = false;
                Assert.False(window.ReplaceAllButton.IsEffectivelyEnabled);
                Assert.True(window.NextMatchButton.IsEffectivelyEnabled);
                search.CanEdit = true;
                window.QueryInput.Text = "(a+)+$";
                Task obsolete = search.Work;
                search.CloseCommand.Execute(null);
                Assert.False(search.IsSearching);
                Assert.True(window.Editor.TextArea.IsFocused);
                await obsolete.ConfigureAwait(true);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.Equal(new string('a', 30000) + "!", window.Editor.Text);
            }
            finally
            {
                window.ViewModel.Text = string.Empty;
                window.Close();
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Open, Reload, and New keep regex state local and preserve Markdown source bytes.</summary>
    [Fact]
    public async Task TransitionsKeepRegexWindowLocalAndSaveExactSource()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.md");
            const string original = "# λ12\r\nλ3\nλ4\r😀";
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
            window.Show();
            try
            {
                await File.WriteAllTextAsync(path, original).ConfigureAwait(true);
                SearchViewModel search = window.ViewModel.Search;
                search.ShowReplaceCommand.Execute(null);
                search.Query = @"λ(\d+)";
                search.Replacement = "猫$1";
                search.UseRegex = true;
                Assert.True(await window.ViewModel.OpenPathAsync(path).ConfigureAwait(true));
                await search.Work.ConfigureAwait(true);
                Assert.True(search.UseRegex);
                Assert.Equal(3, search.MatchCount);
                window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Preview.Work.ConfigureAwait(true);
                TextBlock heading = Assert.IsType<TextBlock>(window.PreviewBlocks.Children[0]);
                Assert.Equal("λ12", heading.Inlines!.Text);
                search.ReplaceAllCommand.Execute(null);
                await search.Work.ConfigureAwait(true);
                await window.Preview.Work.ConfigureAwait(true);
                Assert.Equal("猫12", Assert.IsType<TextBlock>(window.PreviewBlocks.Children[0]).Inlines!.Text);
                Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                Assert.Equal(Encoding.UTF8.GetBytes("# 猫12\r\n猫3\n猫4\r😀"), await File.ReadAllBytesAsync(path).ConfigureAwait(true));
                await File.WriteAllTextAsync(path, original).ConfigureAwait(true);
                Assert.True(await window.ViewModel.ReloadAsync().ConfigureAwait(true));
                await search.Work.ConfigureAwait(true);
                Assert.Equal(original, window.Editor.Text);
                Assert.True(search.UseRegex);
                Assert.Equal(3, search.MatchCount);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.Equal(0, window.Editor.SelectionStart);
                Assert.Equal(0, window.Editor.SelectionLength);
                search.Query = "(";
                Task obsolete = search.Work;
                Assert.True(await window.ViewModel.NewAsync().ConfigureAwait(true));
                await obsolete.ConfigureAwait(true);
                await search.Work.ConfigureAwait(true);
                Assert.True(search.UseRegex);
                Assert.True(search.InvalidPattern);
                Assert.Equal(0, search.MatchCount);
                EditorWindow other = new(new FileTextDocumentStore(), new Dialogs());
                other.Show();
                Assert.False(other.ViewModel.Search.UseRegex);
                other.Close();
            }
            finally
            {
                window.ViewModel.Text = string.Empty;
                window.Close();
                File.Delete(path);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Batch replacement preserves intervening text and earlier undo entries.</summary>
    [Fact]
    public async Task BatchReplacementPreservesGapsAndEarlierHistory()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
            window.Show();
            try
            {
                const string original = "prefix cat1\r\nmiddle 😀 cat22 suffix";
                window.Editor.Text = original;
                window.Editor.Document.UndoStack.ClearAll();
                window.Editor.Document.Insert(0, "!");
                SearchViewModel search = window.ViewModel.Search;
                search.ShowReplaceCommand.Execute(null);
                search.Query = @"cat(\d+)";
                search.Replacement = "$1";
                search.UseRegex = true;
                await search.Work.ConfigureAwait(true);
                search.ReplaceAllCommand.Execute(null);
                await search.Work.ConfigureAwait(true);
                Assert.Equal("!prefix 1\r\nmiddle 😀 22 suffix", window.Editor.Text);
                Assert.Equal("1", window.Editor.SelectedText);
                window.Editor.Document.UndoStack.Undo();
                await search.Work.ConfigureAwait(true);
                Assert.Equal("!" + original, window.Editor.Text);
                Assert.True(window.Editor.Document.UndoStack.CanUndo);
                window.Editor.Document.UndoStack.Undo();
                await search.Work.ConfigureAwait(true);
                Assert.Equal(original, window.Editor.Text);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                window.Editor.Document.UndoStack.Redo();
                window.Editor.Document.UndoStack.Redo();
                await search.Work.ConfigureAwait(true);
                Assert.Equal("!prefix 1\r\nmiddle 😀 22 suffix", window.Editor.Text);
            }
            finally
            {
                window.ViewModel.Text = string.Empty;
                window.Close();
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>A larger replacement remains one undo operation and reports scan and edit timings.</summary>
    [Fact]
    public async Task LargeReplacementBenchmark()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
            window.Show();
            try
            {
                string original = string.Concat(Enumerable.Repeat("entry λ123 lorem ipsum 😀\r\n", 15000));
                window.Editor.Text = original;
                window.Editor.Document.UndoStack.ClearAll();
                SearchViewModel search = window.ViewModel.Search;
                search.ShowReplaceCommand.Execute(null);
                search.Query = @"λ(\d+)";
                search.Replacement = "value=$1";
                System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
                search.UseRegex = true;
                await search.Work.ConfigureAwait(true);
                Assert.Equal(15000, search.MatchCount);
                double scanMilliseconds = elapsed.Elapsed.TotalMilliseconds;
                elapsed.Restart();
                search.ReplaceAllCommand.Execute(null);
                double editMilliseconds = elapsed.Elapsed.TotalMilliseconds;
                Assert.Equal(string.Concat(Enumerable.Repeat("entry value=123 lorem ipsum 😀\r\n", 15000)), window.Editor.Text);
                Assert.Equal(15000, search.ReplacementCount);
                await search.Work.ConfigureAwait(true);
                window.Editor.Document.UndoStack.Undo();
                await search.Work.ConfigureAwait(true);
                Assert.Equal(original, window.Editor.Text);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                TestContext.Current.TestOutputHelper!.WriteLine($"Editor benchmark: {original.Length} UTF-16 units, 15000 matches, scan {scanMilliseconds:F1} ms, edit {editMilliseconds:F1} ms.");
            }
            finally
            {
                window.ViewModel.Text = string.Empty;
                window.Close();
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

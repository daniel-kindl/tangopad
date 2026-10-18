using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using AvaloniaEdit;
using Editor.App.Services;
using Editor.App.Views;
using Editor.App.Views.Dialogs;
using Editor.Infrastructure;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Editor.App.Tests.Views.EditorBindingTests))]

namespace Editor.App.Tests.Views;

/// <summary>Checks compiled views and the AvaloniaEdit control on a headless UI dispatcher.</summary>
public sealed class EditorBindingTests
{
    /// <summary>The document counts start at zero beside the caret status.</summary>
    [Fact]
    public async Task CharacterStatusStartsAtZero()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
            window.Show();
            TextBlock status = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("CharacterStatus"));
            Assert.Equal("Characters: 0", status.Text);
            TextBlock words = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("WordStatus"));
            Assert.Equal("Words: 0", words.Text);
            AssertCaretStatus(window, 1, 1);
            Assert.Equal(string.Empty, window.ViewModel.Text);
            Assert.False(window.ViewModel.IsDirty);
            Assert.False(window.Editor.Document.UndoStack.CanUndo);
            window.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>The count follows editing and document commands without changing selection, history, or saved bytes.</summary>
    /// <param name="dark">Whether the window uses the dark theme.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CharacterStatusPreservesEditingAndPersistence(bool dark)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string path = Path.GetTempFileName();
            try
            {
                const string original = "A😀e\u0301\r\nB\nC\r";
                await File.WriteAllTextAsync(path, original).ConfigureAwait(true);
                EditorWindow window = new(new FileTextDocumentStore(), new Dialogs { OpenPath = path, SavePath = path })
                {
                    RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
                };
                window.Show();
                void AssertCount(int expected, int words)
                {
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(expected, window.ViewModel.CharacterCount);
                    Assert.Equal($"Characters: {expected}", window.CharacterStatus.Text);
                    Assert.Equal(words, window.ViewModel.WordCount);
                    Assert.Equal($"Words: {words}", window.WordStatus.Text);
                }

                Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                Guid identity = window.ViewModel.DocumentId;
                AssertCount(10, 3);
                Assert.False(window.ViewModel.IsDirty);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                window.Editor.CaretOffset = window.Editor.Document.TextLength;
                _ = window.Editor.TextArea.Focus();
                window.KeyTextInput("!");
                AssertCount(11, 4);
                Assert.Equal(original + "!", window.Editor.Text);
                Assert.True(window.ViewModel.IsDirty);
                List<string?> notifications = [];
                window.ViewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
                window.Editor.Select(1, 2);
                int caret = window.Editor.CaretOffset;
                window.ViewModel.WordWrap = true;
                window.ViewModel.IncreaseFontSizeCommand.Execute(null);
                window.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark;
                window.Editor.ScrollToEnd();
                AssertCount(11, 4);
                Assert.DoesNotContain("Text", notifications);
                Assert.DoesNotContain("CharacterCount", notifications);
                Assert.DoesNotContain("WordCount", notifications);
                Assert.Equal("😀", window.Editor.SelectedText);
                Assert.Equal(caret, window.Editor.CaretOffset);
                AssertCaretStatus(window, 1, 4);
                Assert.Equal(identity, window.ViewModel.DocumentId);
                Assert.True(window.Editor.Document.UndoStack.CanUndo);
                window.Editor.Document.UndoStack.Undo();
                AssertCount(10, 3);
                Assert.Equal(original, window.ViewModel.Session.Document.Text);
                Assert.False(window.ViewModel.IsDirty);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.True(window.Editor.Document.UndoStack.CanRedo);
                window.Editor.Document.UndoStack.Redo();
                AssertCount(11, 4);
                window.Editor.CaretOffset = window.Editor.Document.TextLength;
                await window.Clipboard!.SetTextAsync(" pasted words").ConfigureAwait(true);
                window.Editor.Paste();
                AssertCount(24, 6);
                Assert.Equal(original + "! pasted words", window.Editor.Text);
                window.Editor.Document.UndoStack.Undo();
                AssertCount(11, 4);
                Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                AssertCount(11, 4);
                Assert.Equal(identity, window.ViewModel.DocumentId);
                Assert.False(window.ViewModel.IsDirty);
                Assert.True(window.Editor.Document.UndoStack.CanUndo);
                Assert.Equal(System.Text.Encoding.UTF8.GetBytes(original + "!"), await File.ReadAllBytesAsync(path).ConfigureAwait(true));
                window.ViewModel.Search.ShowReplaceCommand.Execute(null);
                window.ViewModel.Search.Query = "😀";
                window.ViewModel.Search.Replacement = "x y ";
                window.ViewModel.Search.ReplaceAllCommand.Execute(null);
                AssertCount(14, 6);
                Assert.Equal("Ax y e\u0301\r\nB\nC\r!", window.Editor.Text);
                window.Editor.Document.UndoStack.Undo();
                AssertCount(11, 4);
                Assert.False(window.ViewModel.IsDirty);
                Assert.True(await window.ViewModel.NewAsync().ConfigureAwait(true));
                AssertCount(0, 0);
                Assert.NotEqual(identity, window.ViewModel.DocumentId);
                Assert.Equal(string.Empty, window.Editor.Text);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.False(window.ViewModel.IsDirty);
                Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                AssertCount(11, 4);
                Assert.Equal(original + "!", window.Editor.Text);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
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

    /// <summary>The status indicator starts at the first line and column without changing an empty document.</summary>
    [Fact]
    public async Task CaretStatusStartsAtOne()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
            window.Show();
            TextBlock status = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("CaretStatus"));
            Assert.Equal("Ln 1, Col 1", status.Text);
            Assert.Equal(string.Empty, window.Editor.Text);
            Assert.Equal(0, window.Editor.CaretOffset);
            Assert.False(window.ViewModel.IsDirty);
            Assert.False(window.Editor.Document.UndoStack.CanUndo);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Caret offsets use one-based document positions across CRLF, LF, and CR line endings.</summary>
    /// <param name="offset">The caret offset in the mixed-newline document.</param>
    /// <param name="line">The expected line.</param>
    /// <param name="column">The expected column.</param>
    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(3, 1, 4)]
    [InlineData(7, 2, 1)]
    [InlineData(9, 2, 3)]
    [InlineData(10, 3, 1)]
    [InlineData(12, 4, 1)]
    [InlineData(15, 4, 4)]
    public async Task CaretStatusTracksMixedNewlinePositions(int offset, int line, int column)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
            window.Show();
            window.Editor.Text = "first\r\nxy\nz\rend";
            window.Editor.Document.UndoStack.ClearAll();
            window.Editor.CaretOffset = offset;
            AssertCaretStatus(window, line, column);
            Assert.Equal(offset, window.Editor.CaretOffset);
            Assert.Equal("first\r\nxy\nz\rend", window.ViewModel.Session.Document.Text);
            Assert.False(window.Editor.Document.UndoStack.CanUndo);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Mouse selection reports the active caret endpoint without changing the selected text.</summary>
    [Fact]
    public async Task CaretStatusTracksMouseSelection()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
            window.Show();
            window.Editor.Text = "first\nsecond";
            Dispatcher.UIThread.RunJobs();
            AvaloniaEdit.Rendering.TextView textView = window.Editor.TextArea.TextView;
            Point start = textView.TranslatePoint(textView.GetVisualPosition(new TextViewPosition(2, 2),
                AvaloniaEdit.Rendering.VisualYPosition.LineMiddle), window)!.Value;
            Point end = textView.TranslatePoint(textView.GetVisualPosition(new TextViewPosition(2, 5),
                AvaloniaEdit.Rendering.VisualYPosition.LineMiddle), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            AssertCaretStatus(window, 2, 2);
            window.MouseMove(end);
            window.MouseUp(end, MouseButton.Left);
            AssertCaretStatus(window, 2, 5);
            Assert.Equal("eco", window.Editor.SelectedText);
            Assert.Equal("first\nsecond", window.ViewModel.Session.Document.Text);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Typing, keyboard selection, search navigation, and undo update the indicator without changing selection.</summary>
    [Fact]
    public async Task CaretStatusTracksEditingAndNavigation()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
            window.Show();
            window.KeyTextInput("one\ntwo");
            AssertCaretStatus(window, 2, 4);
            window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
            AssertCaretStatus(window, 2, 3);
            Assert.Equal("o", window.Editor.SelectedText);
            window.Editor.Document.UndoStack.Undo();
            AssertCaretStatus(window, 1, 1);
            Assert.Equal(string.Empty, window.Editor.Text);
            window.Editor.Document.UndoStack.Redo();
            AssertCaretStatus(window, 2, 4);
            window.ViewModel.Search.ShowFindCommand.Execute(null);
            window.ViewModel.Search.Query = "one";
            window.ViewModel.Search.NextCommand.Execute(null);
            AssertCaretStatus(window, 1, 4);
            Assert.Equal("one", window.Editor.SelectedText);
            Assert.Equal("one\ntwo", window.ViewModel.Session.Document.Text);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            window.Editor.CaretOffset = window.Editor.Document.TextLength;
            Assert.Equal(1, window.ViewModel.CaretLine);
            Assert.Equal(4, window.ViewModel.CaretColumn);
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Compiled bindings disable replacement during Open and expose cancellation without replacing the buffer.</summary>
    [Fact]
    public async Task BusyBindingsPreserveTheEditorBuffer()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            DelayedStore store = new();
            EditorWindow window = new(store, new Dialogs { OpenPath = "/input.txt" });
            window.Show();
            window.Editor.Document.Insert(0, "keep");
            window.Editor.CaretOffset = 2;
            window.ViewModel.Search.ShowReplaceCommand.Execute(null);
            window.ViewModel.Search.Query = "keep";
            window.ViewModel.Search.NextCommand.Execute(null);
            Assert.True(window.ReplaceAllButton.IsEffectivelyEnabled);
            window.Editor.CaretOffset = 2;
            Task opening = window.ViewModel.OpenCommand.ExecuteAsync(null);
            Assert.False(window.NewButton.IsEffectivelyEnabled);
            Assert.True(window.CancelOperation.IsVisible);
            Assert.True(window.Editor.IsReadOnly);
            Assert.False(window.ReplaceAllButton.IsEffectivelyEnabled);
            Assert.False(window.ReplaceCurrentButton.IsEffectivelyEnabled);
            Assert.True(window.Editor.Document.UndoStack.CanUndo);
            Assert.Equal(2, window.Editor.CaretOffset);
            window.CancelOperation.Command!.Execute(null);
            store.Load.SetResult("must not replace");
            await opening.ConfigureAwait(true);
            Assert.Equal("keep", window.Editor.Text);
            Assert.Equal(2, window.Editor.CaretOffset);
            Assert.True(window.NewButton.IsEffectivelyEnabled);
            Assert.False(window.CancelOperation.IsVisible);
            Assert.False(window.Editor.IsReadOnly);
            Assert.True(window.ReplaceAllButton.IsEffectivelyEnabled);
            window.Editor.Document.UndoStack.Undo();
            Assert.Equal(string.Empty, window.ViewModel.Text);
            window.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Semantic resources update an existing window, editor selection, and error text in both themes.</summary>
    [Fact]
    public async Task ThemeResourcesUpdateExistingControls()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            Avalonia.Application application = Avalonia.Application.Current!;
            Assert.Equal(ThemeVariant.Default, application.RequestedThemeVariant);
            application.RequestedThemeVariant = ThemeVariant.Light;
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
            window.Show();
            window.ViewModel.Search.ShowReplaceCommand.Execute(null);
            window.QueryInput.Text = "missing";
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.SearchPanel.IsVisible);
            Assert.True(window.NoMatchesText.IsVisible);
            Assert.Equal("No matches", window.NoMatchesText.Text);
            ErrorDialog error = new("cannot write");
            Task<UnsavedDecision> dialog = error.ShowDialog<UnsavedDecision>(window);
            Color lightBackground = Assert.IsType<ISolidColorBrush>(window.Background, exactMatch: false).Color;
            Color lightStatus = Assert.IsType<ISolidColorBrush>(window.CaretStatus.Foreground, exactMatch: false).Color;
            Color lightError = Assert.IsType<ISolidColorBrush>(error.MessageText.Foreground, exactMatch: false).Color;
            Assert.Equal(Colors.Black, Assert.IsType<ISolidColorBrush>(window.Foreground, exactMatch: false).Color);
            _ = Assert.IsType<IBrush>(window.Editor.TextArea.SelectionBrush, exactMatch: false);
            string[] roles = ["Background", "Surface", "Text.Primary", "Text.Muted", "Text.Disabled", "Accent", "Border", "Focus", "Error", "Selection"];
            foreach (string role in roles)
            {
                Assert.True(application.TryGetResource($"Brush.{role}", ThemeVariant.Light, out object? light));
                _ = Assert.IsType<IBrush>(light, exactMatch: false);
                Assert.True(application.TryGetResource($"Brush.{role}", ThemeVariant.Dark, out object? dark));
                _ = Assert.IsType<IBrush>(dark, exactMatch: false);
            }

            application.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
            Assert.Equal("No matches", window.NoMatchesText.Text);
            Assert.Equal(window.Foreground, window.NoMatchesText.Foreground);
            Assert.Equal(Colors.White, Assert.IsType<ISolidColorBrush>(window.Foreground, exactMatch: false).Color);
            Assert.NotEqual(lightBackground, Assert.IsType<ISolidColorBrush>(window.Background, exactMatch: false).Color);
            Assert.NotEqual(lightStatus, Assert.IsType<ISolidColorBrush>(window.CaretStatus.Foreground, exactMatch: false).Color);
            Assert.NotEqual(lightError, Assert.IsType<ISolidColorBrush>(error.MessageText.Foreground, exactMatch: false).Color);
            Assert.Equal("Untitled - Tangopad", window.Title);
            Assert.Equal(460, error.Width);
            Assert.Equal("cannot write", error.MessageText.Text);
            error.Close();
            Assert.True(dialog.IsCompleted);
            window.Close();
            application.RequestedThemeVariant = ThemeVariant.Default;
            Assert.Equal(ThemeVariant.Default, application.RequestedThemeVariant);
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Typing, loading, saving, undo, and new preserve exact text and dirty state.</summary>
    [Fact]
    public async Task EditorCommandsPreserveExactText()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), $"tangopad-ui-{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(directory);
            try
            {
                string input = Path.Combine(directory, "input.txt");
                string output = Path.Combine(directory, "output.txt");
                await File.WriteAllTextAsync(input, "loaded\r\nnext\nλ").ConfigureAwait(true);
                Dialogs dialogs = new() { OpenPath = input, SavePath = output };
                EditorWindow window = new(new FileTextDocumentStore(), dialogs);
                window.Show();
                _ = window.Editor.TextArea.Focus();
                AssertCaretStatus(window, 1, 1);
                window.KeyTextInput("typed");
                AssertCaretStatus(window, 1, 6);
                Assert.Equal("typed", window.ViewModel.Session.Document.Text);
                Assert.Equal("Untitled * - Tangopad", window.Title);
                Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                Assert.Equal("loaded\r\nnext\nλ", window.Editor.Text);
                AssertCaretStatus(window, 1, 1);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.Equal($"{input} - Tangopad", window.Title);
                window.Editor.CaretOffset = window.Editor.Document.TextLength;
                AssertCaretStatus(window, 3, 2);
                Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                AssertCaretStatus(window, 1, 1);
                window.Editor.Document.Insert(window.Editor.Document.TextLength, "\rfinal");
                window.KeyPressQwerty(PhysicalKey.S, (OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control) | RawInputModifiers.Shift);
                for (int attempt = 0; window.ViewModel.IsBusy && attempt < 100; attempt++)
                {
                    await Task.Delay(10, TestContext.Current.CancellationToken).ConfigureAwait(true);
                }

                Assert.False(window.ViewModel.IsBusy);
                Assert.Equal("loaded\r\nnext\nλ\rfinal", await File.ReadAllTextAsync(output).ConfigureAwait(true));
                Assert.Equal($"{output} - Tangopad", window.Title);
                window.Editor.Document.UndoStack.Undo();
                Assert.True(window.ViewModel.Session.Document.IsDirty);
                Assert.Equal("loaded\r\nnext\nλ", window.ViewModel.Session.Document.Text);
                window.Editor.CaretOffset = window.Editor.Document.TextLength;
                AssertCaretStatus(window, 3, 2);
                Assert.True(await window.ViewModel.NewAsync().ConfigureAwait(true));
                Assert.Equal(string.Empty, window.Editor.Text);
                AssertCaretStatus(window, 1, 1);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.Equal("Untitled - Tangopad", window.Title);
                dialogs.Decision = UnsavedDecision.Cancel;
                window.KeyTextInput("unsaved");
                window.Close();
                Assert.True(window.IsVisible);
                Assert.True(window.ViewModel.Session.Document.IsDirty);
                dialogs.Decision = UnsavedDecision.Discard;
                window.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.False(window.IsVisible);
                return true;
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>The editor window loads one fixed tree, named controls, and production styles.</summary>
    [Fact]
    public async Task EditorWindowLoadsNamedControlsAndThemeResources()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore());
            window.Show();
            Assert.Equal(960, window.Width);
            Assert.Equal(680, window.Height);
            Assert.Equal(400, window.MinWidth);
            Assert.Equal(250, window.MinHeight);
            Assert.Equal("Untitled - Tangopad", window.Title);

            DockPanel layout = Assert.IsType<DockPanel>(window.Content);
            Assert.Equal(4, layout.Children.Count);
            StackPanel toolbar = Assert.IsType<StackPanel>(layout.Children[0]);
            Assert.Equal(Dock.Top, DockPanel.GetDock(toolbar));
            Assert.Equal(Orientation.Horizontal, toolbar.Orientation);
            StackPanel actions = Assert.IsType<StackPanel>(toolbar.Children[0]);
            Assert.Equal(Orientation.Horizontal, actions.Orientation);
            Assert.Equal(8, actions.Spacing);
            Assert.Equal(new Thickness(8), actions.Margin);
            Button cancel = Assert.IsType<Button>(toolbar.Children[1]);
            Assert.Equal("Cancel operation", cancel.Content);
            Assert.False(cancel.IsVisible);
            Border status = Assert.IsType<Border>(layout.Children[2]);
            Assert.Equal(Dock.Bottom, DockPanel.GetDock(status));
            StackPanel statusItems = Assert.IsType<StackPanel>(status.Child);
            Assert.Equal(Orientation.Horizontal, statusItems.Orientation);
            Assert.Equal([window.CaretStatus, window.CharacterStatus, window.WordStatus], statusItems.Children);
            Grid documentLayout = Assert.IsType<Grid>(layout.Children[3]);
            TextEditor editor = Assert.IsType<TextEditor>(documentLayout.Children[0]);
            Assert.Equal(0, Grid.GetColumn(editor));
            Assert.False(window.PreviewScroll.IsVisible);
            Assert.Equal(FontFamily.Parse("monospace"), editor.FontFamily);
            Assert.Equal(15, editor.FontSize);
            Assert.True(editor.ShowLineNumbers);
            Assert.Equal(ScrollBarVisibility.Auto, editor.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Auto, editor.VerticalScrollBarVisibility);
            Assert.Same(editor, window.FindControl<TextEditor>("Editor"));
            Assert.Same(actions, window.FindControl<StackPanel>("Actions"));
            Assert.Same(cancel, window.FindControl<Button>("CancelOperation"));
            string[] labels = [.. actions.Children.Cast<Button>().Select(button => Assert.IsType<string>(button.Content))];
            Assert.Equal(["New", "Open", "Save", "Save As"], labels);
            Assert.Same(actions.Children[0], window.FindControl<Button>("NewButton"));
            Assert.Same(actions.Children[1], window.FindControl<Button>("OpenButton"));
            Assert.Same(actions.Children[2], window.FindControl<Button>("SaveButton"));
            Assert.Same(actions.Children[3], window.FindControl<Button>("SaveAsButton"));

            KeyModifiers command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            KeyGesture[] gestures = [.. window.KeyBindings.Select(binding => Assert.IsType<KeyGesture>(binding.Gesture))];
            Assert.Equal(Key.N, gestures[0].Key);
            Assert.Equal(Key.O, gestures[1].Key);
            Assert.Equal(Key.S, gestures[2].Key);
            Assert.Equal(Key.S, gestures[3].Key);
            Assert.Equal(command, gestures[0].KeyModifiers);
            Assert.Equal(command, gestures[1].KeyModifiers);
            Assert.Equal(command, gestures[2].KeyModifiers);
            Assert.Equal(command | KeyModifiers.Shift, gestures[3].KeyModifiers);
            Assert.Equal(gestures[0].ToString(), ToolTip.GetTip(Assert.IsType<Button>(actions.Children[0])));
            Assert.Equal(gestures[3].ToString(), ToolTip.GetTip(Assert.IsType<Button>(actions.Children[3])));

            Assert.Equal(3, Avalonia.Application.Current!.Styles.Count);
            _ = Assert.IsType<FluentTheme>(Avalonia.Application.Current.Styles[0]);
            Styles editorTheme = Assert.IsType<Styles>(Avalonia.Application.Current.Styles[1]);
            // These keys identify avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml.
            Assert.True(editorTheme.Resources.ContainsKey("SearchPanelFontSize"));
            Assert.True(editorTheme.Resources.ContainsKey("SearchPanelFontFamily"));
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Confirmation and error dialogs load their markup and return the selected decision.</summary>
    [Fact]
    public async Task PromptDialogsLoadCompiledMarkup()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            UnsavedChangesDialog unsaved = new();
            _ = unsaved.ApplyStyling();
            Assert.Equal("Unsaved changes", unsaved.Title);
            Assert.Equal(460, unsaved.Width);
            Assert.Equal(SizeToContent.Height, unsaved.SizeToContent);
            Assert.False(unsaved.CanResize);
            Assert.Equal(WindowStartupLocation.CenterOwner, unsaved.WindowStartupLocation);
            StackPanel panel = Assert.IsType<StackPanel>(unsaved.Content);
            Assert.Equal(new Thickness(20), panel.Margin);
            Assert.Equal(20, panel.Spacing);
            TextBlock prompt = Assert.IsType<TextBlock>(panel.Children[0]);
            Assert.Equal("Save changes before continuing?", prompt.Text);
            Assert.Equal(TextWrapping.Wrap, prompt.TextWrapping);
            StackPanel choices = Assert.IsType<StackPanel>(panel.Children[1]);
            Assert.Equal(Orientation.Horizontal, choices.Orientation);
            Assert.Equal(HorizontalAlignment.Right, choices.HorizontalAlignment);
            Assert.Equal(8, choices.Spacing);
            Button save = Assert.IsType<Button>(choices.Children[0]);
            Button discard = Assert.IsType<Button>(choices.Children[1]);
            Button cancel = Assert.IsType<Button>(choices.Children[2]);
            Assert.Equal("Save", save.Content);
            Assert.Equal("Discard", discard.Content);
            Assert.Equal("Cancel", cancel.Content);
            Assert.False(save.IsCancel);
            Assert.False(discard.IsCancel);
            Assert.True(cancel.IsCancel);
            Assert.Same(save, unsaved.FindControl<Button>("SaveButton"));
            Assert.Same(discard, unsaved.FindControl<Button>("DiscardButton"));
            Assert.Same(cancel, unsaved.FindControl<Button>("CancelButton"));

            EditorWindow owner = new(new FileTextDocumentStore());
            owner.Show();
            Task<UnsavedDecision> saving = unsaved.ShowDialog<UnsavedDecision>(owner);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(UnsavedDecision.Save, await saving.ConfigureAwait(true));

            ErrorDialog error = new("cannot write");
            _ = error.ApplyStyling();
            Assert.Equal("File operation failed", error.Title);
            Assert.Equal(460, error.Width);
            Assert.Equal(SizeToContent.Height, error.SizeToContent);
            Assert.False(error.CanResize);
            Assert.Equal(WindowStartupLocation.CenterOwner, error.WindowStartupLocation);
            StackPanel errorPanel = Assert.IsType<StackPanel>(error.Content);
            TextBlock message = Assert.IsType<TextBlock>(errorPanel.Children[0]);
            Assert.Equal("cannot write", message.Text);
            Assert.Equal(TextWrapping.Wrap, message.TextWrapping);
            StackPanel errorChoices = Assert.IsType<StackPanel>(errorPanel.Children[1]);
            Button ok = Assert.IsType<Button>(errorChoices.Children[0]);
            Assert.Equal("OK", ok.Content);
            Assert.True(ok.IsCancel);
            Assert.Same(message, error.FindControl<TextBlock>("MessageText"));
            Assert.Same(ok, error.FindControl<Button>("OkButton"));
            Task<UnsavedDecision> dismissing = error.ShowDialog<UnsavedDecision>(owner);
            ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(UnsavedDecision.Cancel, await dismissing.ConfigureAwait(true));
            owner.Close();
            Dispatcher.UIThread.RunJobs();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Search shortcuts, compiled bindings, focus, and literal replacements use the real editor.</summary>
    [Fact]
    public async Task SearchPanelNavigatesReplacesAndUndoes()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
            window.Show();
            window.Editor.Text = "😀 banana λ\r\nλ\nλ\rλ";
            window.Editor.Document.UndoStack.ClearAll();
            window.Editor.CaretOffset = 0;
            RawInputModifiers modifier = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            window.KeyPressQwerty(PhysicalKey.F, modifier);
            Assert.True(window.SearchPanel.IsVisible);
            Assert.True(window.QueryInput.IsFocused);
            Assert.Equal("Find", Avalonia.Automation.AutomationProperties.GetName(window.QueryInput));
            window.KeyTextInput("ana");
            Assert.Equal("ana", window.ViewModel.Search.Query);
            Assert.Equal(2, window.ViewModel.Search.MatchCount);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal(4, window.Editor.SelectionStart);
            Assert.Equal("ana", window.Editor.SelectedText);
            Assert.True(window.QueryInput.IsFocused);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
            Assert.Equal(6, window.Editor.SelectionStart);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.False(window.SearchPanel.IsVisible);
            Assert.True(window.Editor.TextArea.IsFocused);
            Assert.False(window.Editor.Document.UndoStack.CanUndo);

            window.KeyPressQwerty(PhysicalKey.H, modifier);
            Assert.True(window.ReplacementInput.IsVisible);
            window.QueryInput.Text = "λ";
            window.ReplacementInput.Text = "$1\\😀\r\n";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, window.ViewModel.Search.MatchCount);
            Assert.True(window.ReplaceAllButton.IsEffectivelyEnabled);
            _ = window.ReplaceAllButton.Focus();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.Equal("😀 banana $1\\😀\r\n\r\n$1\\😀\r\n\n$1\\😀\r\n\r$1\\😀\r\n", window.Editor.Text);
            Assert.Equal(window.Editor.Text, window.ViewModel.Session.Document.Text);
            Assert.Equal(4, window.ViewModel.Search.ReplacementCount);
            Assert.True(window.ViewModel.IsDirty);
            Assert.True(window.NoMatchesText.IsVisible);
            window.Editor.Document.UndoStack.Undo();
            Assert.Equal("😀 banana λ\r\nλ\nλ\rλ", window.Editor.Text);
            Assert.False(window.Editor.Document.UndoStack.CanUndo);
            Assert.Equal(4, window.ViewModel.Search.MatchCount);
            window.Editor.Document.UndoStack.Redo();
            Assert.Equal(0, window.ViewModel.Search.MatchCount);
            _ = window.CloseSearchButton.Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.False(window.SearchPanel.IsVisible);
            Assert.True(window.Editor.TextArea.IsFocused);
            window.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Replace current rejects stale selections and replacement preserves Save and close protection.</summary>
    [Fact]
    public async Task ReplacementPreservesPersistenceAndCloseProtection()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), $"tangopad-search-{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "output.txt");
                Dialogs dialogs = new() { SavePath = path, Decision = UnsavedDecision.Cancel };
                EditorWindow window = new(new FileTextDocumentStore(), dialogs);
                window.Show();
                window.Editor.Text = "one one";
                Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                window.ViewModel.Search.ShowReplaceCommand.Execute(null);
                window.QueryInput.Text = "one";
                window.ReplacementInput.Text = "two";
                Dispatcher.UIThread.RunJobs();
                Assert.False(window.ReplaceCurrentButton.IsEffectivelyEnabled);
                window.ViewModel.Search.NextCommand.Execute(null);
                Assert.True(window.ReplaceCurrentButton.IsEffectivelyEnabled);
                window.Editor.Select(1, 2);
                Assert.False(window.ReplaceCurrentButton.IsEffectivelyEnabled);
                window.ViewModel.Search.NextCommand.Execute(null);
                _ = window.ReplaceCurrentButton.Focus();
                window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
                Assert.Equal("one two", window.Editor.Text);
                Assert.Equal(1, window.ViewModel.Search.ReplacementCount);
                Assert.True(window.ViewModel.IsDirty);
                window.Close();
                Assert.True(window.IsVisible);
                Assert.Equal("one one", await File.ReadAllTextAsync(path).ConfigureAwait(true));
                window.Editor.Document.UndoStack.Undo();
                Assert.False(window.ViewModel.IsDirty);
                window.Editor.Document.UndoStack.Redo();
                Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                Assert.Equal("one two", await File.ReadAllTextAsync(path).ConfigureAwait(true));
                Assert.False(window.ViewModel.IsDirty);
                window.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.False(window.IsVisible);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Navigation brings a result on a long line into view without moving keyboard focus.</summary>
    [Fact]
    public async Task SearchRevealsMatchesBeyondTheHorizontalViewport()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
            window.Show();
            window.Editor.Text = new string('x', 300) + "needle";
            window.Editor.CaretOffset = 0;
            RawInputModifiers modifier = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            window.KeyPressQwerty(PhysicalKey.F, modifier);
            window.QueryInput.Text = "needle";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, window.Editor.HorizontalOffset);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("needle", window.Editor.SelectedText);
            Assert.True(window.Editor.HorizontalOffset > 0);
            Assert.True(window.QueryInput.IsFocused);
            window.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Wrapping changes visual lines and scrolling while preserving text, selection, and undo history.</summary>
    /// <param name="dark">Whether to use the dark theme.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WordWrapPreservesEditingAndPersistence(bool dark)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), $"tangopad-wrap-{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "input.md");
                string original = string.Concat(Enumerable.Repeat("word λ😀 ", 100)) + "\r\nsecond\nthird\rfourth";
                await File.WriteAllTextAsync(path, original).ConfigureAwait(true);
                EditorWindow window = new(new FileTextDocumentStore(), new Dialogs { OpenPath = path, SavePath = path })
                {
                    RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
                };
                window.Show();
                CheckBox toggle = Assert.IsType<CheckBox>(window.FindControl<CheckBox>("WordWrapToggle"));
                Assert.Equal("Word Wrap", toggle.Content);
                Assert.False(toggle.IsChecked);
                Assert.False(window.Editor.WordWrap);
                Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                Guid identity = window.ViewModel.DocumentId;
                window.Editor.Document.Insert(window.Editor.Document.TextLength, "!");
                window.Editor.Select(700, 3);
                int caret = window.Editor.CaretOffset;
                Assert.True(window.ViewModel.IsDirty);
                Dispatcher.UIThread.RunJobs();
                window.Editor.TextArea.Caret.BringCaretToView();
                Dispatcher.UIThread.RunJobs();
                Assert.True(window.Editor.HorizontalOffset > 0);
                _ = toggle.Focus();
                window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Assert.True(window.ViewModel.WordWrap);
                Assert.True(window.Editor.WordWrap);
                Assert.Equal(ScrollBarVisibility.Disabled, window.Editor.HorizontalScrollBarVisibility);
                Assert.Equal(0, window.Editor.HorizontalOffset);
                Assert.True(window.Editor.TextArea.TextView.GetVisualLine(1).TextLines.Count > 1);
                Assert.Equal(original + "!", window.Editor.Text);
                Assert.Equal(original + "!", window.ViewModel.Session.Document.Text);
                Assert.Equal(identity, window.ViewModel.DocumentId);
                Assert.Equal(700, window.Editor.SelectionStart);
                Assert.Equal(3, window.Editor.SelectionLength);
                Assert.Equal(caret, window.Editor.CaretOffset);
                AssertCaretStatus(window, 1, caret + 1);
                Assert.True(window.ViewModel.IsDirty);
                window.Editor.Document.UndoStack.Undo();
                Assert.Equal(original, window.Editor.Text);
                Assert.False(window.ViewModel.IsDirty);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                window.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark;
                Dispatcher.UIThread.RunJobs();
                Assert.True(toggle.IsChecked);
                Assert.True(window.Editor.WordWrap);
                window.ViewModel.WordWrap = false;
                Dispatcher.UIThread.RunJobs();
                Assert.False(toggle.IsChecked);
                Assert.False(window.Editor.WordWrap);
                Assert.Equal(ScrollBarVisibility.Auto, window.Editor.HorizontalScrollBarVisibility);
                _ = Assert.Single(window.Editor.TextArea.TextView.GetVisualLine(1).TextLines);
                Assert.True(window.Editor.Document.UndoStack.CanRedo);
                window.Editor.Document.UndoStack.Redo();
                Assert.Equal(original + "!", window.Editor.Text);
                window.ViewModel.WordWrap = true;
                Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                Assert.Equal(System.Text.Encoding.UTF8.GetBytes(original + "!"), await File.ReadAllBytesAsync(path).ConfigureAwait(true));
                Assert.False(window.ViewModel.IsDirty);
                Assert.True(await window.ViewModel.NewAsync().ConfigureAwait(true));
                Assert.True(window.Editor.WordWrap);
                Assert.True(toggle.IsChecked);
                Assert.Equal(string.Empty, window.Editor.Text);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                Assert.True(window.Editor.WordWrap);
                Assert.Equal(original + "!", window.Editor.Text);
                Assert.False(window.ViewModel.IsDirty);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Creates the same styles as production with a headless platform.</summary>
    /// <returns>The headless application builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure(() => new App(new FileTextDocumentStore()))
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }

    /// <summary>Font shortcuts change the compiled binding only while the editor has focus.</summary>
    /// <param name="key">The logical shortcut key.</param>
    /// <param name="shift">Whether the shortcut includes Shift.</param>
    /// <param name="expected">The resulting font size.</param>
    [Theory]
    [InlineData(Key.OemPlus, false, 16)]
    [InlineData(Key.OemPlus, true, 16)]
    [InlineData(Key.Add, false, 16)]
    [InlineData(Key.OemMinus, false, 14)]
    [InlineData(Key.Subtract, false, 14)]
    [InlineData(Key.D0, false, 15)]
    [InlineData(Key.NumPad0, false, 15)]
    public async Task FontShortcutsRespectFocusAndPlatform(Key key, bool shift, double expected)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
            window.Show();
            KeyModifiers modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            KeyEventArgs Send(KeyModifiers modifiers)
            {
                KeyEventArgs args = new() { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };
                window.Editor.TextArea.RaiseEvent(args);
                Dispatcher.UIThread.RunJobs();
                return args;
            }

            _ = window.Editor.TextArea.Focus();
            Assert.Equal(15, window.Editor.FontSize);
            Assert.False(Send(OperatingSystem.IsMacOS() ? KeyModifiers.Control : KeyModifiers.Meta).Handled);
            Assert.False(Send(modifier | KeyModifiers.Alt).Handled);
            Assert.Equal(15, window.Editor.FontSize);
            if (key is Key.D0 or Key.NumPad0)
            {
                window.ViewModel.IncreaseFontSizeCommand.Execute(null);
                Assert.Equal(16, window.Editor.FontSize);
            }

            Assert.True(Send(modifier | (shift ? KeyModifiers.Shift : KeyModifiers.None)).Handled);
            Assert.Equal(expected, window.Editor.FontSize);
            window.ViewModel.Search.ShowReplaceCommand.Execute(null);
            _ = window.QueryInput.Focus();
            Assert.False(Send(modifier).Handled);
            _ = window.ReplacementInput.Focus();
            Assert.False(Send(modifier).Handled);
            Assert.Equal(expected, window.Editor.FontSize);
            Assert.Equal(string.Empty, window.Editor.Text);
            Assert.False(window.ViewModel.IsDirty);
            Assert.False(window.Editor.Document.UndoStack.CanUndo);
            window.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Real keyboard input preserves editing state and exact save bytes across document changes.</summary>
    /// <param name="dark">Whether the window starts with the dark theme.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FontSizePreservesEditingAndPersistence(bool dark)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string path = Path.GetTempFileName();
            try
            {
                string original = string.Concat(Enumerable.Repeat("word λ😀 ", 100)) + "\r\nsecond\nthird\rfourth";
                await File.WriteAllTextAsync(path, original).ConfigureAwait(true);
                EditorWindow window = new(new FileTextDocumentStore(), new Dialogs { OpenPath = path, SavePath = path })
                {
                    RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
                };
                window.Show();
                Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                Guid identity = window.ViewModel.DocumentId;
                window.Editor.Document.Insert(window.Editor.Document.TextLength, "!");
                window.Editor.Select(700, 3);
                int caret = window.Editor.CaretOffset;
                Dispatcher.UIThread.RunJobs();
                window.Editor.TextArea.Caret.BringCaretToView();
                Dispatcher.UIThread.RunJobs();
                Assert.True(window.Editor.HorizontalOffset > 0);
                double lineHeight = window.Editor.TextArea.TextView.DefaultLineHeight;
                RawInputModifiers modifier = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
                _ = window.Editor.TextArea.Focus();
                for (int index = 0; index < 5; index++)
                {
                    window.KeyPressQwerty(PhysicalKey.Equal, modifier | RawInputModifiers.Shift);
                    window.KeyReleaseQwerty(PhysicalKey.Equal, modifier | RawInputModifiers.Shift);
                }

                Dispatcher.UIThread.RunJobs();
                Assert.Equal(20, window.Editor.FontSize);
                Assert.True(window.Editor.TextArea.TextView.DefaultLineHeight > lineHeight);
                Assert.Equal(original + "!", window.Editor.Text);
                Assert.Equal(identity, window.ViewModel.DocumentId);
                Assert.Equal(700, window.Editor.SelectionStart);
                Assert.Equal(3, window.Editor.SelectionLength);
                Assert.Equal(caret, window.Editor.CaretOffset);
                AssertCaretStatus(window, 1, caret + 1);
                Assert.True(window.ViewModel.IsDirty);
                window.Editor.TextArea.Caret.BringCaretToView();
                Dispatcher.UIThread.RunJobs();
                Assert.True(window.Editor.HorizontalOffset > 0);
                window.Editor.Document.UndoStack.Undo();
                Assert.Equal(original, window.Editor.Text);
                Assert.False(window.ViewModel.IsDirty);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.True(window.Editor.Document.UndoStack.CanRedo);
                window.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark;
                window.ViewModel.WordWrap = true;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(20, window.Editor.FontSize);
                Assert.True(window.Editor.WordWrap);
                window.Editor.Document.UndoStack.Redo();
                Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                Assert.Equal(System.Text.Encoding.UTF8.GetBytes(original + "!"), await File.ReadAllBytesAsync(path).ConfigureAwait(true));
                Assert.False(window.ViewModel.IsDirty);
                Assert.True(await window.ViewModel.NewAsync().ConfigureAwait(true));
                Assert.Equal(20, window.Editor.FontSize);
                Assert.Equal(string.Empty, window.Editor.Text);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                Assert.Equal(20, window.Editor.FontSize);
                Assert.Equal(original + "!", window.Editor.Text);
                Assert.False(window.ViewModel.IsDirty);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                _ = window.Editor.TextArea.Focus();
                window.KeyPressQwerty(PhysicalKey.Digit0, modifier);
                window.KeyReleaseQwerty(PhysicalKey.Digit0, modifier);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(15, window.Editor.FontSize);
                window.Close();
                EditorWindow nextWindow = new(new FileTextDocumentStore(), new Dialogs());
                Assert.Equal(15, nextWindow.Editor.FontSize);
                nextWindow.Close();
            }
            finally
            {
                File.Delete(path);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Go To Line preserves selection on invalid input and cancel, then navigates to logical line starts.</summary>
    /// <param name="wrap">Whether the editor wraps visual lines.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GoToLineUsesLogicalLinesAndPreservesDocument(bool wrap)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
            window.Show();
            string original = new string('x', 1000) + "\r\nsecond\nthird\rfourth\n";
            window.Editor.Text = original;
            window.ViewModel.WordWrap = wrap;
            window.Editor.Document.Insert(0, "!");
            Guid identity = window.ViewModel.DocumentId;
            window.Editor.Select(4, 6);
            int caret = window.Editor.CaretOffset;
            RawInputModifiers modifier = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            Window OpenPrompt()
            {
                _ = window.Editor.TextArea.Focus();
                window.KeyPressQwerty(PhysicalKey.G, modifier);
                window.KeyReleaseQwerty(PhysicalKey.G, modifier);
                Dispatcher.UIThread.RunJobs();
                Window prompt = Assert.Single(window.OwnedWindows);
                Assert.True(prompt.FindControl<TextBox>("LineInput")!.IsFocused);
                Assert.Equal(4, window.Editor.SelectionStart);
                Assert.Equal(6, window.Editor.SelectionLength);
                Assert.Equal(caret, window.Editor.CaretOffset);
                return prompt;
            }

            Window dialog = OpenPrompt();
            TextBox input = dialog.FindControl<TextBox>("LineInput")!;
            string[] invalidEntries = ["", "0", "-1", "1.5", "abc", "2147483648"];
            foreach (string invalid in invalidEntries)
            {
                input.Text = invalid;
                dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                dialog.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Assert.True(dialog.IsVisible);
                Assert.True(dialog.FindControl<TextBlock>("ValidationMessage")!.IsVisible);
                Assert.Equal(caret, window.Editor.CaretOffset);
                Assert.Equal(6, window.Editor.SelectionLength);
            }

            dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.OwnedWindows);
            Assert.True(window.Editor.IsKeyboardFocusWithin);
            Assert.Equal(caret, window.Editor.CaretOffset);
            dialog = OpenPrompt();
            dialog.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(6, window.Editor.SelectionLength);
            Assert.Equal(caret, window.Editor.CaretOffset);
            foreach ((string line, int offset, int expectedLine) in new[] { ("2", 1003, 2), ("3", 1010, 3), ("4", 1016, 4), ("999", 1023, 5), ("1", 0, 1) })
            {
                _ = window.Editor.TextArea.Focus();
                window.KeyPressQwerty(PhysicalKey.G, modifier);
                window.KeyReleaseQwerty(PhysicalKey.G, modifier);
                Dispatcher.UIThread.RunJobs();
                dialog = Assert.Single(window.OwnedWindows);
                dialog.FindControl<TextBox>("LineInput")!.Text = line;
                dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(window.OwnedWindows);
                Assert.Equal(offset, window.Editor.CaretOffset);
                Assert.Equal(0, window.Editor.SelectionLength);
                AssertCaretStatus(window, expectedLine, 1);
                Assert.True(window.Editor.IsKeyboardFocusWithin);
            }

            Assert.Equal("!" + original, window.Editor.Text);
            Assert.Equal("!" + original, window.ViewModel.Session.Document.Text);
            Assert.Equal(identity, window.ViewModel.DocumentId);
            Assert.True(window.ViewModel.IsDirty);
            window.Editor.Document.UndoStack.Undo();
            Assert.Equal(original, window.Editor.Text);
            window.Editor.Document.UndoStack.Redo();
            Assert.Equal("!" + original, window.Editor.Text);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>The shortcut requires editor focus and reveals a distant line without changing clean state.</summary>
    [Fact]
    public async Task GoToLineRespectsFocusAndScrolls()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
            window.Show();
            KeyModifiers modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            KeyEventArgs Send(KeyModifiers modifiers)
            {
                KeyEventArgs args = new() { RoutedEvent = InputElement.KeyDownEvent, Key = Key.G, KeyModifiers = modifiers };
                window.Editor.TextArea.RaiseEvent(args);
                Dispatcher.UIThread.RunJobs();
                return args;
            }

            Assert.False(Send(OperatingSystem.IsMacOS() ? KeyModifiers.Control : KeyModifiers.Meta).Handled);
            Assert.False(Send(modifier | KeyModifiers.Shift).Handled);
            window.ViewModel.Search.ShowReplaceCommand.Execute(null);
            _ = window.QueryInput.Focus();
            Assert.False(Send(modifier).Handled);
            _ = window.ReplacementInput.Focus();
            Assert.False(Send(modifier).Handled);
            Assert.Empty(window.OwnedWindows);
            _ = window.Editor.TextArea.Focus();
            Assert.True(Send(modifier).Handled);
            Window dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal("1", dialog.FindControl<TextBox>("LineInput")!.Text);
            dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.OwnedWindows);
            Assert.Equal(0, window.Editor.CaretOffset);
            Assert.False(window.ViewModel.IsDirty);
            Assert.False(window.Editor.Document.UndoStack.CanUndo);
            window.Editor.Text = string.Concat(Enumerable.Repeat("line\n", 100));
            window.Editor.Document.UndoStack.ClearAll();
            window.Editor.CaretOffset = 0;
            Dispatcher.UIThread.RunJobs();
            Assert.True(Send(modifier).Handled);
            dialog = Assert.Single(window.OwnedWindows);
            dialog.FindControl<TextBox>("LineInput")!.Text = "100";
            dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            AssertCaretStatus(window, 100, 1);
            Assert.Equal(495, window.Editor.CaretOffset);
            Assert.True(window.Editor.VerticalOffset > 0);
            Assert.True(window.Editor.TextArea.TextView.GetVisualLine(100) is not null);
            Assert.False(window.Editor.Document.UndoStack.CanUndo);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static void AssertCaretStatus(EditorWindow window, int line, int column)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(line, window.ViewModel.CaretLine);
        Assert.Equal(column, window.ViewModel.CaretColumn);
        Assert.Equal($"Ln {line}, Col {column}", window.CaretStatus.Text);
    }

    private sealed class Dialogs : IEditorDialogs
    {
        internal UnsavedDecision Decision { get; set; } = UnsavedDecision.Discard;
        internal string? OpenPath { get; init; }
        internal string? SavePath { get; init; }
        public Task<string?> ChooseOpenAsync() { return Task.FromResult(OpenPath); }
        public Task<string?> ChooseSaveAsync(string? destination) { return Task.FromResult(SavePath); }
        public Task<UnsavedDecision> ConfirmUnsavedAsync() { return Task.FromResult(Decision); }
        public Task ShowErrorAsync(string message) { Assert.Fail(message); return Task.CompletedTask; }
    }

    private sealed class DelayedStore : Application.ITextDocumentStore
    {
        internal TaskCompletionSource<string> Load { get; } = new();

        public Task<string> LoadAsync(string destination, CancellationToken cancellationToken = default)
        {
            return Load.Task;
        }

        public Task SaveAsync(string destination, string text, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("This test does not save documents.");
        }
    }
}

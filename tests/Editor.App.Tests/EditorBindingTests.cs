using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using AvaloniaEdit;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests;

/// <summary>Checks compiled views and the AvaloniaEdit control on a headless UI dispatcher.</summary>
public sealed class EditorBindingTests
{
    /// <summary>Compiled bindings disable replacement during Open and expose cancellation without replacing the buffer.</summary>
    [Fact]
    public async Task BusyBindingsPreserveTheEditorBuffer()
    {
        await using HeadlessUnitTestSession ui = HeadlessUnitTestSession.StartNew(typeof(EditorBindingTests));
        _ = await ui.Dispatch(async () =>
        {
            DelayedStore store = new();
            EditorWindow window = new(store, new Dialogs { OpenPath = "/input.txt" });
            window.Show();
            window.Editor.Document.Insert(0, "keep");
            window.Editor.CaretOffset = 2;
            Task opening = window.ViewModel.OpenCommand.ExecuteAsync(null);
            Assert.False(window.NewButton.IsEffectivelyEnabled);
            Assert.True(window.CancelOperation.IsVisible);
            Assert.True(window.Editor.IsReadOnly);
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
        await using HeadlessUnitTestSession ui = HeadlessUnitTestSession.StartNew(typeof(EditorBindingTests));
        _ = await ui.Dispatch(() =>
        {
            Avalonia.Application application = Avalonia.Application.Current!;
            Assert.Equal(ThemeVariant.Default, application.RequestedThemeVariant);
            application.RequestedThemeVariant = ThemeVariant.Light;
            EditorWindow window = new(new FileTextDocumentStore(), new Dialogs());
            window.Show();
            ErrorDialog error = new("cannot write");
            Task<UnsavedDecision> dialog = error.ShowDialog<UnsavedDecision>(window);
            Color lightBackground = Assert.IsType<ISolidColorBrush>(window.Background, exactMatch: false).Color;
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
            Assert.Equal(Colors.White, Assert.IsType<ISolidColorBrush>(window.Foreground, exactMatch: false).Color);
            Assert.NotEqual(lightBackground, Assert.IsType<ISolidColorBrush>(window.Background, exactMatch: false).Color);
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
        await using HeadlessUnitTestSession ui = HeadlessUnitTestSession.StartNew(typeof(EditorBindingTests));
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
                window.KeyTextInput("typed");
                Assert.Equal("typed", window.ViewModel.Session.Document.Text);
                Assert.Equal("Untitled * - Tangopad", window.Title);
                Assert.True(await window.ViewModel.OpenAsync().ConfigureAwait(true));
                Assert.Equal("loaded\r\nnext\nλ", window.Editor.Text);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.Equal($"{input} - Tangopad", window.Title);
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
                Assert.True(await window.ViewModel.NewAsync().ConfigureAwait(true));
                Assert.Equal(string.Empty, window.Editor.Text);
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
        await using HeadlessUnitTestSession ui = HeadlessUnitTestSession.StartNew(typeof(EditorBindingTests));
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
            Assert.Equal(2, layout.Children.Count);
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
            TextEditor editor = Assert.IsType<TextEditor>(layout.Children[1]);
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
        await using HeadlessUnitTestSession ui = HeadlessUnitTestSession.StartNew(typeof(EditorBindingTests));
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

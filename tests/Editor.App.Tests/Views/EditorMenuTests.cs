using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Editor.App.Views;
using Editor.App.Views.Dialogs;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks document actions through the desktop menus.</summary>
public sealed class EditorMenuTests
{
    /// <summary>Menu commands use the existing source buffer and preserve its undo history.</summary>
    [Fact]
    public async Task MenusOperateOnCurrentDocument()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore());
            window.Show();
            Assert.Equal(5, window.MainMenu.Items.Count);
            Assert.Equal("Untitled", window.DocumentName.Text);
            Assert.Same(window.ViewModel.NewCommand, window.NewMenu.Command);
            Assert.Same(window.ViewModel.SaveCommand, window.SaveMenu.Command);
            Assert.Equal(window.NewMenu.InputGesture, window.KeyBindings[0].Gesture);
            Assert.False(window.UndoMenu.Command!.CanExecute(null));
            window.Editor.Document.Insert(0, "a draft");
            window.Editor.Document.Insert(7, "!");
            Assert.True(window.UndoMenu.Command.CanExecute(null));
            window.UndoMenu.Command.Execute(null);
            Assert.Equal("a draft", window.Editor.Text);
            window.RedoMenu.Command!.Execute(null);
            Assert.Equal("a draft!", window.Editor.Text);
            window.SelectAllMenu.Command!.Execute(null);
            Assert.Equal("a draft!", window.Editor.SelectedText);
            window.DarkThemeMenu.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
            Assert.Equal("a draft!", window.Editor.Text);
            Assert.True(window.ViewModel.IsDirty);
            window.LightThemeMenu.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
            window.FindMenu.Command!.Execute(null);
            Assert.True(window.QueryInput.IsKeyboardFocusWithin);
            Assert.True(window.ViewModel.Search.IsOpen);
            window.ViewModel.Search.CloseCommand.Execute(null);
            Assert.True(window.Editor.IsKeyboardFocusWithin);
            window.Editor.Document.UndoStack.Undo();
            window.Editor.Document.UndoStack.Undo();
            Assert.Equal(string.Empty, window.Editor.Text);
            Assert.False(window.ViewModel.IsDirty);
            window.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>About shows the actual build version and restores editor focus after it closes.</summary>
    [Fact]
    public async Task AboutShowsVersionAndCloses()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            EditorWindow window = new(new FileTextDocumentStore());
            window.Show();
            IAsyncRelayCommand command = Assert.IsType<IAsyncRelayCommand>(window.AboutMenu.Command, exactMatch: false);
            Task showing = command.ExecuteAsync(null);
            AboutDialog about = Assert.IsType<AboutDialog>(Assert.Single(window.OwnedWindows));
            Assert.Equal("About Tangopad", about.Title);
            Assert.Equal("Version 0.1.0", about.VersionText.Text);
            about.CloseButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await showing.ConfigureAwait(true);
            Assert.Empty(window.OwnedWindows);
            Assert.True(window.Editor.IsKeyboardFocusWithin);
            Assert.Equal(string.Empty, window.Editor.Text);
            window.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }
}

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Editor.App.Services;
using Editor.App.Views;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks saved preferences through the compiled editor controls.</summary>
public sealed class EditorPreferencesTests
{
    /// <summary>A fresh window restores preferences and preserves early user changes.</summary>
    [Fact]
    public async Task RestoredControlsAndEarlyChanges()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string path = Path.Combine(directory, "preferences.json");
            try
            {
                EditorPreferences first = new(path);
                EditorWindow window = new(new FileTextDocumentStore(), new Dialogs(), editorPreferences: first);
                window.Show();
                await window.ViewModel.InitializePreferencesAsync().ConfigureAwait(true);
                window.ViewModel.WordWrap = true;
                window.ViewModel.ShowLineNumbers = false;
                window.ViewModel.IncreaseFontSizeCommand.Execute(null);
                window.Close();
                await first.FlushAsync().ConfigureAwait(true);
                EditorPreferences second = new(path);
                EditorWindow restored = new(new FileTextDocumentStore(), new Dialogs(), editorPreferences: second);
                await restored.ViewModel.InitializePreferencesAsync().ConfigureAwait(true);
                restored.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(restored.Editor.WordWrap);
                Assert.False(restored.Editor.ShowLineNumbers);
                Assert.Equal(16, restored.Editor.FontSize);
                Assert.False(restored.FindControl<CheckBox>("LineNumbersToggle")!.IsChecked);
                Assert.False(restored.ViewModel.IsDirty);
                Assert.Equal("", restored.Editor.Text);
                restored.Close();

                EditorPreferences third = new(path);
                EditorWindow early = new(new FileTextDocumentStore(), new Dialogs(), editorPreferences: third);
                early.ViewModel.WordWrap = true;
                early.ViewModel.WordWrap = false;
                early.ViewModel.IncreaseFontSizeCommand.Execute(null);
                early.ViewModel.IncreaseFontSizeCommand.Execute(null);
                await early.ViewModel.InitializePreferencesAsync().ConfigureAwait(true);
                early.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.False(early.Editor.WordWrap);
                Assert.False(early.Editor.ShowLineNumbers);
                Assert.Equal(17, early.Editor.FontSize);
                early.Close();
                await third.FlushAsync().ConfigureAwait(true);
                EditorPreferences fourth = new(path);
                EditorWindow reset = new(new FileTextDocumentStore(), new Dialogs(), editorPreferences: fourth);
                reset.ViewModel.ResetFontSizeCommand.Execute(null);
                await reset.ViewModel.InitializePreferencesAsync().ConfigureAwait(true);
                Assert.Equal(15, reset.Editor.FontSize);
                reset.Close();
                await fourth.FlushAsync().ConfigureAwait(true);
            }
            finally
            {
                Directory.Delete(directory, true);
            }

            return true;
        }, TestContext.Current.CancellationToken);
    }

    private sealed class Dialogs : IEditorDialogs
    {
        public Task<string?> ChooseOpenAsync() { return Task.FromResult<string?>(null); }
        public Task<string?> ChooseSaveAsync(string? destination) { return Task.FromResult<string?>(null); }
        public Task<UnsavedDecision> ConfirmUnsavedAsync() { return Task.FromResult(UnsavedDecision.Discard); }
        public Task ShowErrorAsync(string message) { Assert.Fail(message); return Task.CompletedTask; }
    }
}

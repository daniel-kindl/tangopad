using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests;

/// <summary>Checks the actual AvaloniaEdit control on a headless UI dispatcher.</summary>
public sealed class EditorBindingTests
{
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
                Assert.Equal("typed", window.Workspace.Session.Document.Text);
                Assert.Equal("Untitled * - Tangopad", window.Title);
                Assert.True(await window.Workspace.OpenAsync().ConfigureAwait(true));
                Assert.Equal("loaded\r\nnext\nλ", window.Editor.Text);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.Equal($"{input} - Tangopad", window.Title);
                window.Editor.Document.Insert(window.Editor.Document.TextLength, "\rfinal");
                window.KeyPressQwerty(PhysicalKey.S, (OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control) | RawInputModifiers.Shift);
                for (int attempt = 0; window.Workspace.IsBusy && attempt < 100; attempt++)
                {
                    await Task.Delay(10, TestContext.Current.CancellationToken).ConfigureAwait(true);
                }

                Assert.False(window.Workspace.IsBusy);
                Assert.Equal("loaded\r\nnext\nλ\rfinal", await File.ReadAllTextAsync(output).ConfigureAwait(true));
                Assert.Equal($"{output} - Tangopad", window.Title);
                window.Editor.Document.UndoStack.Undo();
                Assert.True(window.Workspace.Session.Document.IsDirty);
                Assert.Equal("loaded\r\nnext\nλ", window.Workspace.Session.Document.Text);
                Assert.True(await window.Workspace.NewAsync().ConfigureAwait(true));
                Assert.Equal(string.Empty, window.Editor.Text);
                Assert.False(window.Editor.Document.UndoStack.CanUndo);
                Assert.Equal("Untitled - Tangopad", window.Title);
                dialogs.Decision = UnsavedDecision.Cancel;
                window.KeyTextInput("unsaved");
                window.Close();
                Assert.True(window.IsVisible);
                Assert.True(window.Workspace.Session.Document.IsDirty);
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
}

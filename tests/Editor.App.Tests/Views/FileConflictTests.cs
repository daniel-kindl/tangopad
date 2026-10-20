using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Editor.App.Services;
using Editor.App.Views;
using Editor.App.Views.Dialogs;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks conflict choices with real files and the headless editor.</summary>
public sealed class FileConflictTests
{
    /// <summary>Cancel preserves the full buffer state. Overwrite persists exact bytes. Reload uses the unsaved guard.</summary>
    /// <param name="choice">The conflict decision.</param>
    /// <param name="unsaved">The reload confirmation decision.</param>
    [Theory]
    [InlineData("Cancel", "Discard")]
    [InlineData("Overwrite", "Discard")]
    [InlineData("Reload", "Discard")]
    [InlineData("Reload", "Cancel")]
    public async Task ConflictChoicesPreserveOrReplaceEditingState(string choice, string unsaved)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string path = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(path, "original\r\n").ConfigureAwait(true);
                Dialogs dialogs = new() { Decision = Enum.Parse<ConflictDecision>(choice), Unsaved = Enum.Parse<UnsavedDecision>(unsaved) };
                EditorWindow window = new(new FileTextDocumentStore(), dialogs);
                window.Show();
                Assert.True(await window.ViewModel.OpenPathAsync(path).ConfigureAwait(true));
                Guid identity = window.ViewModel.DocumentId;
                window.Editor.Document.Insert(0, "😀 λ\n");
                window.Editor.Document.Insert(window.Editor.Document.TextLength, "later");
                window.Editor.Document.UndoStack.Undo();
                window.Editor.Select(2, 3);
                int caret = window.Editor.CaretOffset;
                const string local = "😀 λ\noriginal\r\n";
                await File.WriteAllTextAsync(path, "external\r").ConfigureAwait(true);
                Assert.Equal(choice == "Overwrite", await window.ViewModel.SaveAsync().ConfigureAwait(true));
                Assert.Equal(1, dialogs.Conflicts);
                bool reload = choice == "Reload" && unsaved == "Discard";
                Assert.Equal(reload ? "external\r" : local, window.Editor.Text);
                Assert.Equal(reload, identity != window.ViewModel.DocumentId);
                Assert.Equal(!reload && choice != "Overwrite", window.ViewModel.IsDirty);
                Assert.Equal(Encoding.UTF8.GetBytes(choice == "Overwrite" ? local : "external\r"), await File.ReadAllBytesAsync(path).ConfigureAwait(true));
                if (!reload)
                {
                    Assert.Equal(2, window.Editor.SelectionStart);
                    Assert.Equal(3, window.Editor.SelectionLength);
                    Assert.Equal(caret, window.Editor.CaretOffset);
                    Assert.True(window.Editor.Document.UndoStack.CanUndo);
                    Assert.True(window.Editor.Document.UndoStack.CanRedo);
                }
                else
                {
                    Assert.Equal(0, window.Editor.CaretOffset);
                    Assert.False(window.Editor.Document.UndoStack.CanUndo);
                }

                dialogs.Unsaved = UnsavedDecision.Discard;
                Assert.True(await window.ViewModel.NewAsync().ConfigureAwait(true));
                window.Close();
            }
            finally
            {
                File.Delete(path);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>New edits or operation cancellation during confirmation prevent a later overwrite.</summary>
    /// <param name="cancel">Whether to cancel the operation instead of editing.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingConfirmationCannotOverwriteNewerState(bool cancel)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "original", TestContext.Current.CancellationToken);
            Dialogs dialogs = new() { Pending = new() };
            Editor.App.ViewModels.EditorViewModel model = new(new FileTextDocumentStore(), dialogs, new("Untitled", "{0}{1}", " *"));
            Assert.True(await model.OpenPathAsync(path).ConfigureAwait(true));
            model.Text = "snapshot";
            Guid identity = model.DocumentId;
            await File.WriteAllTextAsync(path, "external", TestContext.Current.CancellationToken);
            Task<bool> save = model.SaveAsync();
            await dialogs.Entered.Task.WaitAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.True(model.IsBusy);
            Assert.True(model.CanEdit);
            Assert.False(await model.SaveAsync().ConfigureAwait(true));
            if (cancel) { model.Cancel(); }
            else { model.Text = "newer"; }
            dialogs.Pending.SetResult(ConflictDecision.Overwrite);
            Assert.False(await save.ConfigureAwait(true));
            Assert.Equal(cancel ? "snapshot" : "newer", model.Text);
            Assert.Equal(identity, model.DocumentId);
            Assert.True(model.IsDirty);
            Assert.Equal("external"u8.ToArray(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Save before Open or Reload stops replacement on conflict and loads exact saved bytes after success.</summary>
    /// <param name="reload">Whether the requested replacement is Reload.</param>
    /// <param name="external">Whether another writer changes the current file.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SaveBeforeReplacementHonorsConflict(bool reload, bool external)
    {
        string path = Path.GetTempFileName();
        string target = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "original", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(target, "target", TestContext.Current.CancellationToken);
            Dialogs dialogs = new() { Unsaved = UnsavedDecision.Save };
            Editor.App.ViewModels.EditorViewModel model = new(new FileTextDocumentStore(), dialogs, new("Untitled", "{0}{1}", " *"));
            Assert.True(await model.OpenPathAsync(path).ConfigureAwait(true));
            Guid identity = model.DocumentId;
            model.Text = "local";
            if (external)
            {
                await File.WriteAllTextAsync(path, "external", TestContext.Current.CancellationToken);
            }

            Assert.Equal(!external, await (reload ? model.ReloadAsync() : model.OpenPathAsync(target)).ConfigureAwait(true));
            Assert.Equal(external, identity == model.DocumentId);
            Assert.Equal(external, model.IsDirty);
            Assert.Equal(external || reload ? "local" : "target", model.Text);
            Assert.Equal(external || reload ? path : target, model.Session.Destination);
            Assert.Equal(Encoding.UTF8.GetBytes(external ? "external" : "local"), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal("target"u8.ToArray(), await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
            File.Delete(target);
        }
    }

    /// <summary>The owned dialog returns each explicit decision and defaults to Cancel.</summary>
    /// <param name="button">The clicked button.</param>
    /// <param name="expected">The expected result.</param>
    [Theory]
    [InlineData("CancelButton", "Cancel")]
    [InlineData("ReloadButton", "Reload")]
    [InlineData("OverwriteButton", "Overwrite")]
    [InlineData("Escape", "Cancel")]
    [InlineData("Enter", "Cancel")]
    [InlineData("Close", "Cancel")]
    public async Task DialogReturnsExplicitDecision(string button, string expected)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            Window owner = new();
            owner.Show();
            FileConflictDialog dialog = new("/current.txt");
            Task<ConflictDecision> result = dialog.ShowDialog<ConflictDecision>(owner);
            Assert.Equal("/current.txt", dialog.DestinationText.Text);
            Assert.True(dialog.CancelButton.IsDefault);
            Assert.True(dialog.CancelButton.IsCancel);
            if (button == "Close")
            {
                dialog.Close();
            }
            else if (button is "Escape" or "Enter")
            {
                dialog.KeyPress(Enum.Parse<Key>(button), RawInputModifiers.None, Enum.Parse<PhysicalKey>(button), null);
                owner.KeyRelease(Enum.Parse<Key>(button), RawInputModifiers.None, Enum.Parse<PhysicalKey>(button), null);
            }
            else
            {
                dialog.FindControl<Button>(button)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Assert.Equal(Enum.Parse<ConflictDecision>(expected), await result.ConfigureAwait(true));
            owner.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private sealed class Dialogs : IEditorDialogs
    {
        internal ConflictDecision Decision { get; init; }
        internal UnsavedDecision Unsaved { get; set; } = UnsavedDecision.Discard;
        internal TaskCompletionSource<ConflictDecision>? Pending { get; init; }
        internal TaskCompletionSource Entered { get; } = new();
        internal int Conflicts { get; private set; }
        public Task<string?> ChooseOpenAsync() { return Task.FromResult<string?>(null); }
        public Task<string?> ChooseSaveAsync(string? destination) { return Task.FromResult<string?>(null); }
        public Task<UnsavedDecision> ConfirmUnsavedAsync() { return Task.FromResult(Unsaved); }
        public Task<ConflictDecision> ConfirmConflictAsync(string destination)
        {
            Conflicts++;
            _ = Entered.TrySetResult();
            return Pending?.Task ?? Task.FromResult(Decision);
        }

        public Task ShowErrorAsync(string message) { Assert.Fail(message); return Task.CompletedTask; }
    }
}

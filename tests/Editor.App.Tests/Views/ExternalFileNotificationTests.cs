using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using CommunityToolkit.Mvvm.Input;
using Editor.App.Services;
using Editor.App.Views;
using Editor.Application;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks disk notifications through the editor and injected file events.</summary>
public sealed class ExternalFileNotificationTests
{
    /// <summary>A disk change must expose an accessible banner without changing the buffer.</summary>
    [Fact]
    public async Task ExternalChangePreservesBufferAndDismissDoesNotPermitOverwrite()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string path = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(path, "# original\r\n").ConfigureAwait(true);
                Dialogs dialogs = new();
                EditorWindow window = new(new FileTextDocumentStore(), dialogs);
                window.Show();
                Assert.True(await window.ViewModel.OpenPathAsync(path).ConfigureAwait(true));
                window.Editor.Document.Insert(0, "edit");
                window.Editor.Document.UndoStack.Undo();
                window.Editor.Select(2, 3);
                Guid identity = window.ViewModel.DocumentId;
                await File.WriteAllTextAsync(path, "external").ConfigureAwait(true);
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
                while (window.FindControl<WrapPanel>("ExternalChangeBanner")?.IsVisible != true)
                {
                    await Task.Delay(10, timeout.Token).ConfigureAwait(true);
                }
                Assert.Equal("# original\r\n", window.Editor.Text);
                Assert.Equal(identity, window.ViewModel.DocumentId);
                Assert.Equal(2, window.Editor.SelectionStart);
                Assert.Equal(3, window.Editor.SelectionLength);
                Assert.True(window.Editor.Document.UndoStack.CanRedo);
                Assert.False(window.ViewModel.IsDirty);
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(window.FindControl<TextBlock>("ExternalChangeText")!));
                window.FindControl<Button>("DismissExternalChangeButton")!.Command!.Execute(null);
                Assert.False(window.FindControl<WrapPanel>("ExternalChangeBanner")!.IsVisible);
                Assert.False(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                Assert.Equal(1, dialogs.Conflicts);
                Assert.Equal("external", await File.ReadAllTextAsync(path).ConfigureAwait(true));
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

    /// <summary>Injected bursts and revisions cover dismissal, coalescing, and later changes.</summary>
    /// <param name="change">The external filesystem operation.</param>
    [Theory]
    [InlineData("edit")]
    [InlineData("delete")]
    [InlineData("rename")]
    [InlineData("replace")]
    public async Task VerifiedEventsAndDismissal(string change)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string directory = Directory.CreateTempSubdirectory("tangopad-monitor-").FullName;
            string path = Path.Combine(directory, "current.md");
            try
            {
                await File.WriteAllTextAsync(path, "# original").ConfigureAwait(true);
                TaskCompletionSource<string?> initial = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Monitor monitor = new() { Pending = initial };
                EditorWindow window = new(new FileTextDocumentStore(), new Dialogs(), fileMonitor: monitor);
                window.Show();
                Assert.True(await window.ViewModel.OpenPathAsync(path).ConfigureAwait(true));
                await WaitAsync(() => monitor.Reads > 0).ConfigureAwait(true);
                Assert.NotNull(window.ViewModel.Session.Revision);
                monitor.UseRevision(window.ViewModel.Session.Revision);
                initial.SetResult(window.ViewModel.Session.Revision);
                monitor.Pending = null;
                await WaitForDeliveryAsync(window, 0).ConfigureAwait(true);
                Assert.False(window.ViewModel.HasExternalChange);
                Action signal = monitor.Signal!;
                await VerifyEventsAsync(monitor, window).ConfigureAwait(true);
                Assert.False(window.ViewModel.HasExternalChange);
                if (change == "edit")
                {
                    await File.WriteAllTextAsync(path, "# external").ConfigureAwait(true);
                }
                else
                {
                    File.Move(path, Path.Combine(directory, "renamed.md"));
                    if (change == "replace")
                    {
                        await File.WriteAllTextAsync(path, "replacement").ConfigureAwait(true);
                    }
                }

                string? revision = change switch
                {
                    "delete" or "rename" => null,
                    "replace" => "replacement",
                    _ => "external",
                };
                int reads = monitor.Reads;
                TaskCompletionSource<string?> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                monitor.Pending = gate;
                for (int i = 0; i < 1000; i++) { signal(); }
                await WaitAsync(() => monitor.Reads > reads).ConfigureAwait(true);
                Assert.Equal(reads + 1, monitor.Reads);
                monitor.UseRevision(revision);
                monitor.Pending = null;
                gate.SetResult(revision);
                await WaitAsync(() => window.ViewModel.HasExternalChange).ConfigureAwait(true);
                Assert.Equal(reads + 1, monitor.Reads);
                Assert.Equal("# original", window.Editor.Text);
                window.ViewModel.DismissExternalChangeCommand.Execute(null);
                await VerifyEventsAsync(monitor, window).ConfigureAwait(true);
                Assert.False(window.ViewModel.HasExternalChange);
                await File.WriteAllTextAsync(path, "another revision").ConfigureAwait(true);
                monitor.UseRevision("another");
                signal();
                await WaitAsync(() => window.ViewModel.HasExternalChange).ConfigureAwait(true);
                Assert.True(await window.ViewModel.ReloadAsync().ConfigureAwait(true));
                Assert.False(window.ViewModel.HasExternalChange);
                Assert.Equal("another revision", window.Editor.Text);
                signal();
                Assert.True(await window.ViewModel.NewAsync().ConfigureAwait(true));
                signal();
                Assert.False(window.ViewModel.HasExternalChange);
                window.Close();
                await window.ExternalChanges.Work.ConfigureAwait(true);
                Assert.Equal(0, monitor.Active);
            }
            finally { Directory.Delete(directory, true); }
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Transitions reject late verification and keep failed Open, own saves, and Save As current.</summary>
    [Fact]
    public async Task TransitionsRejectStaleResults()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string directory = Directory.CreateTempSubdirectory("tangopad-monitor-").FullName;
            string path = Path.Combine(directory, "first.md");
            string second = Path.Combine(directory, "second.txt");
            try
            {
                await File.WriteAllTextAsync(path, "original").ConfigureAwait(true);
                Monitor monitor = new();
                Dialogs dialogs = new() { SavePath = second };
                EditorWindow window = new(new FileTextDocumentStore(), dialogs, fileMonitor: monitor);
                window.Show();
                Assert.True(await window.ViewModel.OpenPathAsync(path).ConfigureAwait(true));
                TaskCompletionSource<string?> late = new(TaskCreationOptions.RunContinuationsAsynchronously);
                monitor.Pending = late;
                Action oldSignal = monitor.Signal!;
                await WaitAsync(() => monitor.Reads > 0).ConfigureAwait(true);
                Task oldWork = window.ExternalChanges.Work;
                Assert.True(await window.ViewModel.NewAsync().ConfigureAwait(true));
                late.SetResult("stale");
                await oldWork.ConfigureAwait(true);
                oldSignal();
                Assert.False(window.ViewModel.HasExternalChange);
                monitor.Pending = null;
                Assert.True(await window.ViewModel.OpenPathAsync(path).ConfigureAwait(true));
                await File.WriteAllTextAsync(path, "external").ConfigureAwait(true);
                monitor.Signal!();
                await WaitAsync(() => window.ViewModel.HasExternalChange).ConfigureAwait(true);
                Assert.False(await window.ViewModel.OpenPathAsync(Path.Combine(directory, "missing")).ConfigureAwait(true));
                Assert.True(window.ViewModel.HasExternalChange);
                Assert.Equal(path, monitor.Path);
                Assert.True(await window.ViewModel.SaveAsAsync().ConfigureAwait(true));
                Assert.False(window.ViewModel.HasExternalChange);
                Assert.Equal(second, monitor.Path);
                window.Editor.Document.Insert(0, "local ");
                Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                await VerifyEventsAsync(monitor, window).ConfigureAwait(true);
                Assert.False(window.ViewModel.HasExternalChange);
                window.Close();
                await window.ExternalChanges.Work.ConfigureAwait(true);
                monitor.Signal!();
                Assert.Equal(0, monitor.Active);
            }
            finally { Directory.Delete(directory, true); }
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Monitoring failures do not block editing, saving, or guarded reload.</summary>
    /// <param name="watchFails">Whether subscription creation fails.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MonitoringErrorsAreOptional(bool watchFails)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string path = Path.GetTempFileName();
            try
            {
                Monitor monitor = new() { WatchFails = watchFails, ReadFails = true };
                EditorWindow window = new(new FileTextDocumentStore(), new Dialogs(), fileMonitor: monitor);
                window.Show();
                Assert.True(await window.ViewModel.OpenPathAsync(path).ConfigureAwait(true));
                await WaitAsync(() => monitor.Reads > 0).ConfigureAwait(true);
                Assert.False(window.ViewModel.HasExternalChange);
                window.Editor.Document.Insert(0, "local");
                Assert.True(await window.ViewModel.SaveAsync().ConfigureAwait(true));
                monitor.ReadFails = false;
                await File.WriteAllTextAsync(path, "external").ConfigureAwait(true);
                monitor.Signal?.Invoke();
                await WaitAsync(() => window.ViewModel.HasExternalChange).ConfigureAwait(true);
                Assert.True(await window.ViewModel.ReloadAsync().ConfigureAwait(true));
                Assert.Equal("external", window.Editor.Text);
                window.Close();
                await window.ExternalChanges.Work.ConfigureAwait(true);
            }
            finally { File.Delete(path); }
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>The banner Reload action retains Save, Discard, Cancel, and conflict decisions.</summary>
    /// <param name="choice">The unsaved-change decision.</param>
    [Theory]
    [InlineData("Cancel")]
    [InlineData("Discard")]
    [InlineData("Save")]
    public async Task BannerReloadUsesUnsavedGuard(string choice)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string path = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(path, "original").ConfigureAwait(true);
                Monitor monitor = new();
                UnsavedDecision decision = Enum.Parse<UnsavedDecision>(choice);
                Dialogs dialogs = new() { Unsaved = decision };
                EditorWindow window = new(new FileTextDocumentStore(), dialogs, fileMonitor: monitor);
                window.Show();
                Assert.True(await window.ViewModel.OpenPathAsync(path).ConfigureAwait(true));
                window.Editor.Document.Insert(0, "local ");
                await File.WriteAllTextAsync(path, "external").ConfigureAwait(true);
                monitor.Signal!();
                await WaitAsync(() => window.ViewModel.HasExternalChange).ConfigureAwait(true);
                await Assert.IsType<IAsyncRelayCommand>(window.FindControl<Button>("ReloadExternalChangeButton")!.Command, exactMatch: false).ExecuteAsync(null).ConfigureAwait(true);
                await WaitAsync(() => !window.ViewModel.IsBusy).ConfigureAwait(true);
                Assert.Equal(decision == UnsavedDecision.Discard ? "external" : "local original", window.Editor.Text);
                Assert.Equal(decision != UnsavedDecision.Discard, window.ViewModel.IsDirty);
                Assert.Equal(decision == UnsavedDecision.Save ? 1 : 0, dialogs.Conflicts);
                Assert.Equal("external", await File.ReadAllTextAsync(path).ConfigureAwait(true));
                dialogs.Unsaved = UnsavedDecision.Discard;
                Assert.True(await window.ViewModel.NewAsync().ConfigureAwait(true));
                window.Close();
            }
            finally { File.Delete(path); }
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Each file transition and window close rejects a result from the previous subscription.</summary>
    /// <param name="transition">The operation while verification remains pending.</param>
    [Theory]
    [InlineData("new")]
    [InlineData("open")]
    [InlineData("reload")]
    [InlineData("save-as")]
    [InlineData("close")]
    public async Task LateVerificationCannotAffectAnotherSubscription(string transition)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string directory = Directory.CreateTempSubdirectory("tangopad-monitor-").FullName;
            string first = Path.Combine(directory, "first.txt");
            string second = Path.Combine(directory, "second.txt");
            try
            {
                await File.WriteAllTextAsync(first, "first").ConfigureAwait(true);
                await File.WriteAllTextAsync(second, "second").ConfigureAwait(true);
                TaskCompletionSource<string?> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Monitor monitor = new() { Pending = pending };
                Dialogs dialogs = new() { SavePath = Path.Combine(directory, "saved.txt") };
                EditorWindow window = new(new FileTextDocumentStore(), dialogs, fileMonitor: monitor);
                window.Show();
                Assert.True(await window.ViewModel.OpenPathAsync(first).ConfigureAwait(true));
                await WaitAsync(() => monitor.Reads > 0).ConfigureAwait(true);
                Task previous = window.ExternalChanges.Work;
                monitor.Pending = null;
                bool success = transition switch
                {
                    "new" => await window.ViewModel.NewAsync().ConfigureAwait(true),
                    "open" => await window.ViewModel.OpenPathAsync(second).ConfigureAwait(true),
                    "reload" => await window.ViewModel.ReloadAsync().ConfigureAwait(true),
                    "save-as" => await window.ViewModel.SaveAsAsync().ConfigureAwait(true),
                    _ => true,
                };
                Assert.True(success);
                if (transition == "close") { window.Close(); }
                pending.SetResult("obsolete revision");
                await previous.ConfigureAwait(true);
                Assert.False(window.ViewModel.HasExternalChange);
                if (transition != "close") { window.Close(); }
                await window.ExternalChanges.Work.ConfigureAwait(true);
                Assert.Equal(0, monitor.Active);
            }
            finally { Directory.Delete(directory, true); }
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static async Task VerifyEventsAsync(Monitor monitor, EditorWindow window)
    {
        // The dispatcher applies each signal before the next signal starts.
        for (int check = 0; check < 2; check++)
        {
            int delivered = window.ExternalChanges.DeliveredResults;
            monitor.Signal!();
            await WaitForDeliveryAsync(window, delivered).ConfigureAwait(true);
        }
    }

    private static async Task WaitForDeliveryAsync(EditorWindow window, int delivered)
    {
        await WaitAsync(() => window.ExternalChanges.DeliveredResults > delivered).ConfigureAwait(true);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!condition()) { await Task.Delay(10, timeout.Token).ConfigureAwait(true); }
    }

    private sealed class Monitor : IDocumentFileMonitor
    {
        private int reads;
        private volatile string? scriptedRevision;
        private volatile bool scriptRevisions;
        internal int Reads => Volatile.Read(ref reads);
        internal void UseRevision(string? revision)
        {
            scriptedRevision = revision;
            scriptRevisions = true;
        }
        internal int Active { get; private set; }
        internal string? Path { get; private set; }
        internal Action? Signal { get; private set; }
        internal bool WatchFails { get; init; }
        internal bool ReadFails { get; set; }
        internal TaskCompletionSource<string?>? Pending { get; set; }
        public IDisposable Watch(string destination, Action changed)
        {
            Signal = changed;
            Path = destination;
            if (WatchFails) { throw new IOException("Monitoring is unavailable."); }
            Active++;
            Assert.Equal(1, Active);
            return new Subscription(this);
        }
        public Task<string?> ReadRevisionAsync(string destination, CancellationToken cancellationToken)
        {
            Task<string?>? pending = Pending?.Task;
            _ = Interlocked.Increment(ref reads);
            return ReadFails
                ? throw new IOException("Verification is unavailable.")
                : pending ?? (scriptRevisions
                    ? Task.FromResult(scriptedRevision)
                    : new LocalDocumentFileMonitor().ReadRevisionAsync(destination, cancellationToken));
        }
        private sealed class Subscription(Monitor owner) : IDisposable
        {
            public void Dispose() { owner.Active--; }
        }
    }

    private sealed class Dialogs : IEditorDialogs
    {
        internal int Conflicts { get; private set; }
        internal string? SavePath { get; init; }
        internal UnsavedDecision Unsaved { get; set; } = UnsavedDecision.Discard;
        public Task<string?> ChooseOpenAsync() { return Task.FromResult<string?>(null); }
        public Task<string?> ChooseSaveAsync(string? destination) { return Task.FromResult(SavePath ?? destination); }
        public Task<UnsavedDecision> ConfirmUnsavedAsync() { return Task.FromResult(Unsaved); }
        public Task<ConflictDecision> ConfirmConflictAsync(string destination) { Conflicts++; return Task.FromResult(ConflictDecision.Cancel); }
        public Task ShowErrorAsync(string message) { return Task.CompletedTask; }
    }
}

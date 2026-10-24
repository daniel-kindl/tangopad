using Editor.App.Resources;
using Editor.App.ViewModels;
using Editor.Application.Updates;
using Xunit;

namespace Editor.App.Tests.ViewModels;

/// <summary>Checks update commands without restarting the test process.</summary>
public sealed class UpdateViewModelTests
{
    private static readonly UpdateLabels Labels = new("ready", "unsupported", "checking", "{0}: {1}", "current", "downloading", "applying", "cancelled", "failed");

    /// <summary>A newer version exposes an install command. A declined restart keeps the application running.</summary>
    [Fact]
    public async Task DeclinedRestartDoesNotApply()
    {
        Updater updater = new();
        UpdateViewModel model = new(updater, Labels, _ => Task.FromResult(false));
        Assert.False(model.InstallCommand.CanExecute(null));
        await model.CheckCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("0.2.0: 123", model.Status);
        Assert.True(model.InstallCommand.CanExecute(null));
        await model.InstallCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(updater.Downloaded);
        Assert.False(updater.Applied);
        Assert.Equal("cancelled", model.Status);
        Assert.True(model.InstallCommand.CanExecute(null));
    }

    /// <summary>A confirmed restart applies only after download completion.</summary>
    [Fact]
    public async Task ConfirmedRestartAppliesVerifiedDownload()
    {
        Updater updater = new();
        UpdateViewModel model = new(updater, Labels, async apply => { await apply().ConfigureAwait(true); return true; });
        await model.CheckCommand.ExecuteAsync(null).ConfigureAwait(true);
        await model.InstallCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(updater.Applied);
        Assert.Equal("applying", model.Status);
    }

    /// <summary>Feed errors permit a retry. Empty feeds report the installed version as current.</summary>
    [Fact]
    public async Task OfflineAndNoUpdateKeepEditingAvailable()
    {
        Updater updater = new() { Error = new HttpRequestException("offline") };
        UpdateViewModel model = new(updater, Labels, _ => Task.FromResult(false));
        await model.CheckCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("failed", model.Status);
        Assert.Equal("offline", model.Error);
        Assert.True(model.CheckCommand.CanExecute(null));
        Assert.False(model.InstallCommand.CanExecute(null));
        updater.Error = null;
        updater.Release = null;
        await model.CheckCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("current", model.Status);
        Assert.Equal(string.Empty, model.Error);
        Assert.False(updater.Applied);
    }

    /// <summary>Download progress reaches observable state while cancellation remains available.</summary>
    [Fact]
    public async Task DownloadProgressIsObservable()
    {
        Updater updater = new() { WaitForCancellation = true };
        UpdateViewModel model = new(updater, Labels, _ => Task.FromResult(false));
        TaskCompletionSource progress = new();
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(UpdateViewModel.Progress) && model.Progress == 50) { _ = progress.TrySetResult(); }
        };
        await model.CheckCommand.ExecuteAsync(null).ConfigureAwait(true);
        Task pending = model.InstallCommand.ExecuteAsync(null);
        try
        {
            await progress.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Equal(50, model.Progress);
            Assert.Equal("downloading", model.Status);
            Assert.True(model.IsBusy);
        }
        finally
        {
            model.CancelCommand.Execute(null);
            await pending.ConfigureAwait(true);
        }
    }

    /// <summary>Cancellation prevents apply, and concurrent update commands stay disabled.</summary>
    [Fact]
    public async Task CancelPendingDownload()
    {
        Updater updater = new() { WaitForCancellation = true };
        UpdateViewModel model = new(updater, Labels, async apply => { await apply().ConfigureAwait(true); return true; });
        await model.CheckCommand.ExecuteAsync(null).ConfigureAwait(true);
        Task pending = model.InstallCommand.ExecuteAsync(null);
        Assert.True(model.IsBusy);
        Assert.False(model.CheckCommand.CanExecute(null));
        Assert.False(model.InstallCommand.CanExecute(null));
        model.CancelCommand.Execute(null);
        await pending.ConfigureAwait(true);
        Assert.Equal("cancelled", model.Status);
        Assert.False(updater.Applied);
        Assert.False(model.IsBusy);
    }

    private sealed class Updater : IApplicationUpdater
    {
        public string InstalledVersion => "0.1.0";
        public bool IsSupported => true;
        internal UpdateRelease? Release { get; set; } = new("0.2.0", "notes", 123);
        internal Exception? Error { get; set; }
        internal bool WaitForCancellation { get; init; }
        internal bool Downloaded { get; private set; }
        internal bool Applied { get; private set; }
        public Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken)
        {
            return Error is { } error ? Task.FromException<UpdateRelease?>(error) : Task.FromResult(Release);
        }
        public async Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)
        {
            progress.Report(50);
            if (WaitForCancellation) { await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(true); }
            Downloaded = true;
        }
        public void ApplyAndRestart()
        {
            Assert.True(Downloaded);
            Applied = true;
        }
    }
}

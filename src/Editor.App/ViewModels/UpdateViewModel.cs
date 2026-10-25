using System.Globalization;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editor.App.Resources;
using Editor.Application.Updates;

namespace Editor.App.ViewModels;

internal sealed partial class UpdateViewModel(IApplicationUpdater updater, UpdateLabels labels, Func<Func<Task>, Task<bool>> restart) : ObservableObject
{
    private CancellationTokenSource? operation;
    public string InstalledVersion => updater.InstalledVersion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(InstallCommand), nameof(CancelCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    public partial UpdateRelease? Release { get; private set; }

    [ObservableProperty]
    public partial string Status { get; private set; } = updater.IsSupported ? labels.Ready : labels.Unsupported;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int Progress { get; private set; }

    private bool CanCheck => !IsBusy && updater.IsSupported;
    private bool CanInstall => CanCheck && Release is not null;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private Task CheckAsync()
    {
        return RunAsync(async token =>
        {
            Release = null;
            Status = labels.Checking;
            Release = await updater.CheckAsync(token).ConfigureAwait(true);
            Status = Release is null ? labels.Current : string.Format(CultureInfo.CurrentCulture, labels.Available, Release.Version, Release.DownloadBytes);
        });
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private Task InstallAsync()
    {
        return RunAsync(async token =>
        {
            Status = labels.Downloading;
            Progress = 0;
            await updater.DownloadAsync(new Progress<int>(value => Progress = value), token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            bool applied = await restart(() =>
            {
                token.ThrowIfCancellationRequested();
                Status = labels.Applying;
                updater.ApplyAndRestart();
                return Task.CompletedTask;
            }).ConfigureAwait(true);
            if (!applied) { Status = labels.Cancelled; }
        });
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        operation?.Cancel();
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy) { return; }
        using CancellationTokenSource source = new();
        operation = source;
        IsBusy = true;
        Error = string.Empty;
        try { await action(source.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { Status = labels.Cancelled; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException or CryptographicException or InvalidOperationException or System.Text.Json.JsonException or ArgumentException)
        {
            Status = labels.Failed;
            Error = error.Message;
        }
        finally { operation = null; IsBusy = false; }
    }
}

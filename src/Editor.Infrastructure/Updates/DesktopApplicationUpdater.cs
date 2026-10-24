using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Editor.Application.Updates;
using Velopack;
using Velopack.Exceptions;
using Velopack.Locators;

namespace Editor.Infrastructure.Updates;

/// <summary>Integrates signed stable feeds with the native Velopack package updater.</summary>
public sealed class DesktopApplicationUpdater : IApplicationUpdater
{
    private readonly string? publicKey;
    private readonly string[] restartArguments;
    private readonly IVelopackLocator locator;
    private readonly HttpClient? transport;
    private readonly string packageId;
    private UpdateManager? manager;
    private UpdateInfo? pending;
    private bool downloaded;
    private int active;

    /// <summary>Creates the updater for the current process and retains explicit data-mode switches.</summary>
    /// <param name="args">The application startup arguments.</param>
    public DesktopApplicationUpdater(string[] args) : this(args, null, "TangopadDesktop")
    {
    }

    internal DesktopApplicationUpdater(string[] args, HttpClient? transport, string packageId)
    {
        this.transport = transport;
        this.packageId = packageId;
        locator = VelopackLocator.Current;
        using Stream? key = typeof(DesktopApplicationUpdater).Assembly.GetManifestResourceStream("Tangopad.UpdatePublicKey");
        if (key is not null)
        {
            using StreamReader reader = new(key);
            publicKey = reader.ReadToEnd();
        }
        restartArguments = [.. args.Where(arg => arg is "--portable" or "--installed")];
    }

    /// <summary>Waits for an active native update before application state or UI initialization.</summary>
    public static string? WaitForPendingUpdate()
    {
        string? expected = GetDistributionDirectory() is { } root ? UpdateHandoff.WaitForCompletion(root, TimeSpan.FromSeconds(60)) : null;
        return expected is not null && expected != VelopackLocator.Current.CurrentlyInstalledVersion?.ToString()
            ? "The update did not finish. Tangopad started the previous version. Your application data is preserved. Retry the update."
            : null;
    }

    /// <summary>Gets the stable distribution root outside replaceable application content.</summary>
    public static string? GetDistributionDirectory()
    {
        IVelopackLocator current = VelopackLocator.Current;
        return current.AppId is null || current.CurrentlyInstalledVersion is null
            ? null
            : OperatingSystem.IsWindows() ? current.RootAppDir
            : OperatingSystem.IsLinux() && current is LinuxVelopackLocator linux && linux.AppImagePath is { } appImage ? Path.GetDirectoryName(appImage)
            : UserDataLocation.DistributionDirectory(Environment.ProcessPath ?? throw new IOException("The executable location is unavailable."));
    }

    /// <inheritdoc />
    public string InstalledVersion => locator.CurrentlyInstalledVersion?.ToString()
        ?? typeof(DesktopApplicationUpdater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.1.0";

    private static string Channel => $"{(OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux")}-{(RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x64" : "arm64")}";

    /// <inheritdoc />
    public bool IsSupported => publicKey is not null && locator.AppId == packageId && locator.CurrentlyInstalledVersion is not null
        && locator.Channel == Channel && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64
        && Environment.GetEnvironmentVariable("FLATPAK_ID") is null && Environment.GetEnvironmentVariable("SNAP") is null;

    /// <inheritdoc />
    public async Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        Enter();
        try
        {
            pending = null;
            downloaded = false;
            if (!IsSupported || publicKey is not { } key) { throw new InvalidOperationException("This distribution does not support in-app updates."); }
            manager = new UpdateManager(new SignedReleaseSource(key, Channel, cancellationToken, transport, packageId),
                new UpdateOptions { ExplicitChannel = Channel, AllowVersionDowngrade = false, MaximumDeltasBeforeFallback = -1 }, locator);
            UpdateInfo? available = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (available is null || available.IsDowngrade || available.TargetFullRelease.Version <= manager.CurrentVersion) { return null; }
            pending = available;
            VelopackAsset asset = available.TargetFullRelease;
            return new UpdateRelease(asset.Version.ToString(), asset.NotesMarkdown ?? string.Empty, asset.Size);
        }
        catch (Exception error) when (error is AcquireLockFailedException or ChecksumFailedException or NotInstalledException or System.ComponentModel.Win32Exception)
        {
            throw new IOException("The native update operation failed. " + error.Message, error);
        }
        finally { Volatile.Write(ref active, 0); }
    }

    /// <inheritdoc />
    public async Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        Enter();
        try
        {
            downloaded = false;
            if (manager is null || pending is null) { throw new InvalidOperationException("Check for an update before downloading it."); }
            await manager.DownloadUpdatesAsync(pending, progress.Report, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            VerifyStagedPackage();
            downloaded = true;
        }
        catch (Exception error) when (error is AcquireLockFailedException or ChecksumFailedException or NotInstalledException or System.ComponentModel.Win32Exception)
        {
            throw new IOException("The native update operation failed. " + error.Message, error);
        }
        finally { Volatile.Write(ref active, 0); }
    }

    /// <inheritdoc />
    public void ApplyAndRestart()
    {
        Enter();
        try
        {
            if (!downloaded || manager is null || pending is null) { throw new InvalidOperationException("Download and verify the update before applying it."); }
            VerifyStagedPackage();
            // Fail before the native updater can request elevation for a protected installation.
            string root = GetDistributionDirectory() ?? throw new IOException("The distribution location is unavailable.");
            string probe = Path.Combine(root, $".tangopad-update-{Guid.NewGuid():N}");
            using (FileStream permission = new(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            if (OperatingSystem.IsMacOS())
            {
                string manifest = Path.Combine(locator.RootAppDir ?? throw new IOException("The application bundle is unavailable."), "Contents", "Info.plist");
                using FileStream permission = new(manifest, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            }
            using UpdateHandoff handoff = UpdateHandoff.Begin(root, pending.TargetFullRelease.Version.ToString());
            manager.WaitExitThenApplyUpdates(pending.TargetFullRelease, silent: true, restart: true, restartArgs: restartArguments);
            Environment.Exit(0);
        }
        catch (Exception error) when (error is AcquireLockFailedException or ChecksumFailedException or NotInstalledException or System.ComponentModel.Win32Exception)
        {
            throw new IOException("The native update operation failed. " + error.Message, error);
        }
        finally { Volatile.Write(ref active, 0); }
    }

    private void VerifyStagedPackage()
    {
        VelopackAsset asset = pending!.TargetFullRelease;
        string path = Path.Combine(locator.PackagesDir ?? throw new IOException("The update package directory is unavailable."), asset.FileName);
        bool valid;
        using (FileStream package = File.OpenRead(path))
        {
            valid = package.Length == asset.Size && CryptographicOperations.FixedTimeEquals(SHA256.HashData(package), Convert.FromHexString(asset.SHA256));
        }
        if (!valid)
        {
            File.Delete(path);
            throw new CryptographicException("The staged update package does not match its authenticated checksum.");
        }
    }

    private void Enter()
    {
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0) { throw new InvalidOperationException("Another update operation is active."); }
    }
}

namespace Editor.Application.Updates;

/// <summary>A stable release authenticated by the update adapter.</summary>
/// <param name="version">The available stable version.</param>
/// <param name="notes">Plain release notes for display.</param>
/// <param name="downloadBytes">The full package size.</param>
public sealed class UpdateRelease(string version, string notes, long downloadBytes)
{
    /// <summary>Gets the available stable version.</summary>
    public string Version { get; } = version;

    /// <summary>Gets plain release notes for display.</summary>
    public string Notes { get; } = notes;

    /// <summary>Gets the full package size.</summary>
    public long DownloadBytes { get; } = downloadBytes;
}

/// <summary>Checks, stages, and applies authenticated updates for a self-managed installation.</summary>
public interface IApplicationUpdater
{
    /// <summary>Gets the running package version.</summary>
    string InstalledVersion { get; }

    /// <summary>Gets whether this distribution supports authenticated application-controlled updates.</summary>
    bool IsSupported { get; }

    /// <summary>Checks the stable feed without installing an update.</summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A newer authenticated release, or null.</returns>
    Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Downloads and verifies the release from the last successful check.</summary>
    /// <param name="progress">Receives download percentages.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken);

    /// <summary>Verifies the staged package again, then applies it and restarts. The caller must first protect unsaved edits and flush state.</summary>
    void ApplyAndRestart();
}

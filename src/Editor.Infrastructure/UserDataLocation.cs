namespace Editor.Infrastructure;

/// <summary>Resolves installed and explicitly selected portable storage without using the working directory.</summary>
public static class UserDataLocation
{
    /// <summary>Resolves state for this process. The --installed switch overrides a portable marker.</summary>
    /// <param name="args">Application arguments. Conflicting mode switches are rejected.</param>
    /// <param name="distributionDirectory">The package distribution root, outside replaceable application content.</param>
    /// <returns>The absolute binary state path.</returns>
    public static string StatePath(string[] args, string? distributionDirectory = null)
    {
        string executable = Environment.ProcessPath ?? throw new IOException("The executable location is unavailable.");
        string distribution = distributionDirectory ?? DistributionDirectory(executable);
        string platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        return ResolveState(platform, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"), distribution,
            File.Exists(Path.Combine(distribution, "tangopad.portable")), args);
    }

    internal static string DistributionDirectory(string executable)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        // A macOS bundle keeps durable portable data beside the bundle, never inside it.
        if (Path.GetFileName(directory) == "MacOS" && Path.GetFileName(Path.GetDirectoryName(directory)) == "Contents")
        {
            string bundle = Path.GetDirectoryName(Path.GetDirectoryName(directory))!;
            if (bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) { return Path.GetDirectoryName(bundle)!; }
        }
        return directory;
    }

    internal static string ResolveState(string platform, string home, string localData, string? xdgData,
        string distribution, bool marker, string[] args)
    {
        bool portable = args.Contains("--portable", StringComparer.Ordinal);
        bool installed = args.Contains("--installed", StringComparer.Ordinal);
        if (portable && installed) { throw new ArgumentException("Use only one of --portable and --installed.", nameof(args)); }
        if (portable || (marker && !installed)) { return Path.Combine(distribution, "data", "state.bin"); }
        string root = platform switch
        {
            "windows" => localData,
            "macos" => Path.Combine(home, "Library", "Application Support"),
            "linux" => !string.IsNullOrEmpty(xdgData) && Path.IsPathFullyQualified(xdgData) ? xdgData : Path.Combine(home, ".local", "share"),
            _ => throw new ArgumentException("The storage platform is not supported.", nameof(platform))
        };
        return (string.IsNullOrEmpty(home) && platform != "windows") || !Path.IsPathFullyQualified(root)
            ? throw new IOException("The per-user application-data directory is unavailable.")
            : Path.Combine(root, "Tangopad", "state.bin");
    }
}

using Xunit;

namespace Editor.Infrastructure.Tests;

/// <summary>Checks platform roots, portable precedence, bundle placement, and installation isolation.</summary>
public sealed class UserDataLocationTests
{
    /// <summary>Installed mode uses the platform root even when the distribution is writable.</summary>
    [Theory]
    [InlineData("windows")]
    [InlineData("linux")]
    [InlineData("macos")]
    public void InstalledRoots(string platform)
    {
        string home = Path.Combine(Path.GetTempPath(), "home");
        string local = Path.Combine(home, "local-data");
        string xdg = Path.Combine(home, "xdg-data");
        string expected = platform switch
        {
            "windows" => Path.Combine(local, "Tangopad", "state.bin"),
            "linux" => Path.Combine(xdg, "Tangopad", "state.bin"),
            _ => Path.Combine(home, "Library", "Application Support", "Tangopad", "state.bin")
        };
        Assert.Equal(expected, UserDataLocation.ResolveState(platform, home, local, xdg, Path.GetTempPath(), false, []));
        if (platform == "linux")
        {
            Assert.Equal(Path.Combine(home, ".local", "share", "Tangopad", "state.bin"),
                UserDataLocation.ResolveState(platform, home, local, "relative", Path.GetTempPath(), false, []));
        }
    }

    /// <summary>Explicit installed mode overrides a marker, and conflicting switches fail.</summary>
    [Fact]
    public void ModePrecedenceAndInstallationIsolation()
    {
        string root = Path.GetTempPath();
        string first = Path.Combine(root, "first");
        string second = Path.Combine(root, "second");
        string portable = Path.Combine(first, "data", "state.bin");
        Assert.Equal(portable, UserDataLocation.ResolveState("linux", root, root, null, first, true, []));
        Assert.Equal(portable, UserDataLocation.ResolveState("linux", root, root, null, first, false, ["--portable"]));
        Assert.Equal(Path.Combine(root, ".local", "share", "Tangopad", "state.bin"),
            UserDataLocation.ResolveState("linux", root, root, null, first, true, ["--installed"]));
        Assert.NotEqual(portable, UserDataLocation.ResolveState("linux", root, root, null, second, true, []));
        _ = Assert.Throws<ArgumentException>(() => UserDataLocation.ResolveState("linux", root, root, null, first, true, ["--portable", "--installed"]));
        _ = Assert.Throws<IOException>(() => UserDataLocation.ResolveState("windows", root, "", null, first, false, []));
    }

    /// <summary>Executable and bundle paths place portable state beside the distribution.</summary>
    [Fact]
    public void DistributionLocationIgnoresWorkingDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "distribution");
        Assert.Equal(root, UserDataLocation.DistributionDirectory(Path.Combine(root, "Tangopad")));
        Assert.Equal(root, UserDataLocation.DistributionDirectory(Path.Combine(root, "Tangopad.app", "Contents", "MacOS", "Tangopad")));
    }
}

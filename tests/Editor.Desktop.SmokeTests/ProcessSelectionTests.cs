using Xunit;

namespace Editor.Desktop.SmokeTests;

/// <summary>Checks how the native startup test selects the executable and the window owner.</summary>
public sealed class ProcessSelectionTests
{
    /// <summary>An installed package executable replaces the build output only when the variable names one.</summary>
    [Fact]
    public void ExecutableOverrideSelectsTheInstalledPackage()
    {
        string installed = Path.Combine(Path.GetTempPath(), "installed", "Tangopad.AppImage");
        Assert.Equal(installed, NativeShellSmoke.ResolveExecutable(installed, "/build", "Editor.App"));
        Assert.Equal(Path.Combine("/build", "Editor.App"), NativeShellSmoke.ResolveExecutable(null, "/build", "Editor.App"));
        Assert.Equal(Path.Combine("/build", "Editor.App"), NativeShellSmoke.ResolveExecutable(" ", "/build", "Editor.App"));
    }

    /// <summary>A launcher such as an AppImage runtime can own the window through a child process.</summary>
    [Fact]
    public void WindowOwnerIncludesDescendantsOnly()
    {
        (long Id, long Parent)[] processes = [(10, 1), (11, 10), (12, 11), (20, 1), (21, 20)];
        Assert.Equal([10, 11, 12], ProcessTree.WithDescendants(10, processes).Order());
        Assert.Equal([12], ProcessTree.WithDescendants(12, processes).Order());
    }

    /// <summary>On Linux the X11 window owner set reads /proc and includes a child that the launcher started.</summary>
    [Fact]
    public void LinuxWindowOwnersIncludeARealChildProcess()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("This check reads the Linux process table.");
            return;
        }

        using System.Diagnostics.Process child = System.Diagnostics.Process.Start("sleep", "30");
        try
        {
            HashSet<long> owners = X11WindowProbe.WindowOwners(Environment.ProcessId);
            Assert.Contains(Environment.ProcessId, owners);
            Assert.Contains(child.Id, owners);
        }
        finally
        {
            child.Kill();
            _ = child.WaitForExit(TimeSpan.FromSeconds(10));
        }
    }
}

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
}

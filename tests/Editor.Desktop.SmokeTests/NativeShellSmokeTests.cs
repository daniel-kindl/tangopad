using Xunit;

namespace Editor.Desktop.SmokeTests;

/// <summary>Checks that the desktop editor opens a native window and closes.</summary>
public sealed class NativeShellSmokeTests
{
    /// <summary>Starts the built shell, closes its native window, and requires exit code 0.</summary>
    /// <returns>A task that completes when the shell has exited or the check has failed.</returns>
    [Fact]
    public async Task EditorStartsAndCloses()
    {
        NativeStartupResult result = await NativeShellSmoke.RunAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        TestContext.Current.TestOutputHelper?.WriteLine(result.Detail);
        Assert.True(result.Status == NativeStartupStatus.Passed, result.Detail);
    }
}

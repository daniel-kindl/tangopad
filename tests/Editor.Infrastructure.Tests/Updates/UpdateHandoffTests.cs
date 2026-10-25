using System.ComponentModel;
using Editor.Infrastructure.Updates;
using Xunit;

namespace Editor.Infrastructure.Tests.Updates;

/// <summary>Checks startup exclusion and recovery around native update handoff.</summary>
public sealed class UpdateHandoffTests
{
    /// <summary>A missing Unix owner permits recovery. Other native query errors remain visible.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(13)]
    public void OwnerQueryErrorsPreserveTheStartupGate(int nativeError)
    {
        Win32Exception error = new(nativeError);
        if (nativeError == 3 && !OperatingSystem.IsWindows())
        {
            Assert.False(UpdateHandoff.IsOwnerRunning(() => throw error));
        }
        else
        {
            Assert.Same(error, Assert.Throws<Win32Exception>(() => UpdateHandoff.IsOwnerRunning(() => throw error)));
        }
    }

    /// <summary>An active handoff blocks a new editor before it can contain unsaved edits.</summary>
    [Fact]
    public void ActiveHandoffPreventsStartup()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(root);
        try
        {
            using (UpdateHandoff handoff = UpdateHandoff.Begin(root, "0.2.0"))
            {
                _ = Assert.Throws<IOException>(() => UpdateHandoff.WaitForCompletion(root, TimeSpan.Zero));
            }
            Assert.Null(UpdateHandoff.WaitForCompletion(root, TimeSpan.Zero));
            Assert.False(File.Exists(Path.Combine(root, ".tangopad-update-pending")));
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>Recovery reports the attempted version after the initiating process stops.</summary>
    [Fact]
    public void CompletedHandoffReportsTargetVersion()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, ".tangopad-update-pending"), "2147483647\n1\n0.2.0\n");
            Assert.Equal("0.2.0", UpdateHandoff.WaitForCompletion(root, TimeSpan.Zero));
            Assert.False(File.Exists(Path.Combine(root, ".tangopad-update-pending")));
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>A crashed initiating process does not prevent the previous working version from starting.</summary>
    [Fact]
    public void StaleHandoffAllowsRecovery()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, ".tangopad-update-pending"), "2147483647\n1\n");
            Assert.Null(UpdateHandoff.WaitForCompletion(root, TimeSpan.Zero));
            Assert.False(File.Exists(Path.Combine(root, ".tangopad-update-pending")));
        }
        finally { Directory.Delete(root, true); }
    }
}

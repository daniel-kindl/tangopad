#if TANGOPAD_UPDATE_TEST
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Editor.Application.Updates;
using Editor.Infrastructure.Updates;

[assembly: AssemblyMetadata("TangopadUpdateTest", "Tangopad.UpdateTestHook.v1")]

namespace Editor.App.UpdateTest;

/// <summary>
/// Test-only start hook. It exists only in update-test packages (TangopadUpdateTest=true), which
/// use a per-run update key. It runs the editor's own update check, download, signature check
/// and apply against a local feed, then reports the version after the native restart.
/// </summary>
internal static class UpdateTestHook
{
    /// <summary>The marker that the package scan finds in update-test packages.</summary>
    internal const string Marker = "Tangopad.UpdateTestHook.v1";

    private const string PendingName = ".tangopad-update-test-restart";

    /// <summary>Runs a requested update-test action. Returns true when the process must exit.</summary>
    internal static bool TryRun(string[] args)
    {
        string? root = DesktopApplicationUpdater.GetDistributionDirectory();
        string? report = Variable("TANGOPAD_UPDATE_TEST_REPORT");
        if (report is not null)
        {
            Write(report, "report", new DesktopApplicationUpdater(args).InstalledVersion, null);
            return true;
        }
        string? pending = root is null ? null : Path.Combine(root, PendingName);
        if (pending is not null && File.Exists(pending))
        {
            string target = File.ReadAllText(pending).Trim();
            File.Delete(pending);
            Write(target, "restarted", new DesktopApplicationUpdater(args).InstalledVersion, null);
            return true;
        }
        string? result = Variable("TANGOPAD_UPDATE_TEST_RUN");
        if (result is null) { return false; }
        Run(args, result, pending);
        return true;
    }

    private static void Run(string[] args, string result, string? pending)
    {
        string version = Variable("TANGOPAD_UPDATE_TEST_VERSION") ?? throw new InvalidOperationException("Set TANGOPAD_UPDATE_TEST_VERSION.");
        int port = int.Parse(Variable("TANGOPAD_UPDATE_TEST_PORT") ?? throw new InvalidOperationException("Set TANGOPAD_UPDATE_TEST_PORT."), CultureInfo.InvariantCulture);
        string channel = $"{(OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux")}-{(RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x64" : "arm64")}";
        using HttpClientHandler direct = new() { AllowAutoRedirect = false, UseProxy = false };
        using LoopbackForwarder forwarder = new(port, LoopbackForwarder.ExpectedNames(channel, version), direct);
        using HttpClient client = new(forwarder, disposeHandler: false);
        DesktopApplicationUpdater updater = new(args, client, "TangopadDesktop");
        Write(result, "start", updater.InstalledVersion, null);
        try
        {
            UpdateRelease? release = updater.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (release is null)
            {
                Write(result, "no-update", updater.InstalledVersion, null);
                return;
            }
            updater.DownloadAsync(new Progress<int>(), CancellationToken.None).GetAwaiter().GetResult();
            Write(result, "downloaded", release.Version, null);
            if (pending is null) { throw new IOException("The distribution location is unavailable."); }
            File.WriteAllText(pending, result);
            Write(result, "applying", release.Version, null);
            updater.ApplyAndRestart();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (pending is not null) { File.Delete(pending); }
            Write(result, "error", updater.InstalledVersion, error.GetType().Name + ": " + error.Message);
        }
    }

    private static string? Variable(string name)
    {
        return Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
    }

    private static void Write(string path, string step, string version, string? error)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("Event", step);
            writer.WriteString("Marker", Marker);
            writer.WriteString("Version", version);
            writer.WriteNumber("ProcessId", Environment.ProcessId);
            writer.WriteString("ProcessPath", Environment.ProcessPath);
            writer.WriteString("Error", error);
            writer.WriteEndObject();
        }
        buffer.WriteByte((byte)'\n');
        using FileStream output = new(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        output.Write(buffer.ToArray());
    }
}
#endif

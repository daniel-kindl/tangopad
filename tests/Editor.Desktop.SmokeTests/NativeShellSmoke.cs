using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Editor.Desktop.SmokeTests;

internal static class NativeShellSmoke
{
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(15);

    internal static async Task<NativeStartupResult> RunAsync(CancellationToken cancellationToken)
    {
        string directory = ResolveAppDirectory();
        string executableName = OperatingSystem.IsWindows() ? "Editor.App.exe" : "Editor.App";
        string executable = Path.Combine(directory, executableName);
        return !File.Exists(executable)
            ? WithHost(NativeStartupResult.NotTested(
                $"The shell executable is absent at {executable}.",
                "Build Editor.App in the same configuration as this test. Then run dotnet msbuild eng/verify.proj."))
            : true switch
            {
                _ when OperatingSystem.IsLinux() => await RunLinuxAsync(executable, directory, cancellationToken).ConfigureAwait(false),
                _ when OperatingSystem.IsWindows() => await RunWindowsAsync(executable, directory, cancellationToken).ConfigureAwait(false),
                _ when OperatingSystem.IsMacOS() => await RunMacOsAsync(executable, directory, cancellationToken).ConfigureAwait(false),
                _ => WithHost(NativeStartupResult.NotTested(
                    $"The operating system is outside the desktop baseline. Kernel: {RuntimeInformation.OSDescription}.",
                    "Run this check on Linux, Windows, or macOS.")),
            };
    }

    [SupportedOSPlatform("linux")]
    private static async Task<NativeStartupResult> RunLinuxAsync(
        string executable,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (IntPtr.Size != 8)
        {
            return WithHost(NativeStartupResult.NotTested(
                "The X11 close path uses the 64-bit Xlib event layout.",
                "Run this check on a 64-bit Linux host."));
        }

        X11DisplaySession? session;
        string? unavailableReason;
        try
        {
            (session, unavailableReason) = await X11WindowProbe.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DllNotFoundException)
        {
            return WithHost(NativeStartupResult.NotTested(
                "libX11 is not installed.",
                "Install libX11, libICE, libSM, and fontconfig. Then run dotnet msbuild eng/verify.proj."));
        }

        if (session is null)
        {
            return WithHost(NativeStartupResult.NotTested(
                unavailableReason ?? "No X11 display is open.",
                "Install Xvfb or start an X11 display. Install libX11, libICE, libSM, and fontconfig. Then run dotnet msbuild eng/verify.proj."));
        }

        await using (session.ConfigureAwait(false))
        {
            ShellLaunch shell = ShellLaunch.Start(executable, workingDirectory, session.Name);
            await using (shell.ConfigureAwait(false))
            {
                bool closed = await WaitForWindowAsync(
                    () => X11WindowProbe.TrySendDelete(session.Handle, shell.Id),
                    shell,
                    cancellationToken).ConfigureAwait(false);
                return !closed
                    ? await WindowDidNotCloseAsync(shell, "WM_DELETE_WINDOW", null).ConfigureAwait(false)
                    : await WaitForCleanExitAsync(shell, "WM_DELETE_WINDOW", session.Label).ConfigureAwait(false);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<NativeStartupResult> RunWindowsAsync(
        string executable,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ShellLaunch shell = ShellLaunch.Start(executable, workingDirectory, null);
        await using (shell.ConfigureAwait(false))
        {
            bool closed = await WaitForWindowAsync(
                () => Win32WindowProbe.TrySendClose(shell.Id),
                shell,
                cancellationToken).ConfigureAwait(false);
            return !closed
                ? await WindowDidNotCloseAsync(shell, "WM_CLOSE", null).ConfigureAwait(false)
                : await WaitForCleanExitAsync(shell, "WM_CLOSE", "Win32 desktop").ConfigureAwait(false);
        }
    }

    [SupportedOSPlatform("macos")]
    private static async Task<NativeStartupResult> RunMacOsAsync(
        string executable,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (!File.Exists("/usr/bin/osascript"))
        {
            return WithHost(NativeStartupResult.NotTested(
                "osascript is not installed.",
                "Install the macOS scripting tools. Then run dotnet msbuild eng/verify.proj on an interactive desktop."));
        }

        ShellLaunch shell = ShellLaunch.Start(executable, workingDirectory, null);
        await using (shell.ConfigureAwait(false))
        {
            string? lastProbe = null;
            long deadline = Environment.TickCount64 + (long)WindowTimeout.TotalMilliseconds;
            bool closed = false;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (shell.HasExited)
                {
                    break;
                }

                lastProbe = await MacOsWindowProbe.TrySendCloseAsync(shell.Id, cancellationToken).ConfigureAwait(false);
                if (lastProbe is null)
                {
                    closed = true;
                    break;
                }

                if (IsAccessibilityBlock(lastProbe))
                {
                    return WithHost(NativeStartupResult.NotTested(
                        "macOS denied the window close. " + lastProbe,
                        "Grant assistive access to the session that runs osascript. Then run dotnet msbuild eng/verify.proj on an interactive desktop."));
                }

                if (Environment.TickCount64 >= deadline)
                {
                    break;
                }

                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }

            if (!closed)
            {
                string probeDetail = string.IsNullOrWhiteSpace(lastProbe) ? "The macOS close probe reported no window." : lastProbe;
                return await WindowDidNotCloseAsync(shell, "AXCloseButton", probeDetail).ConfigureAwait(false);
            }

            return await WaitForCleanExitAsync(shell, "AXCloseButton", "macOS desktop").ConfigureAwait(false);
        }
    }

    private static bool IsAccessibilityBlock(string probe)
    {
        return probe.Contains("assistive", StringComparison.OrdinalIgnoreCase)
            || probe.Contains("-25211", StringComparison.Ordinal)
            || probe.Contains("-1719", StringComparison.Ordinal);
    }

    private static async Task<bool> WaitForWindowAsync(
        Func<bool> tryClose,
        ShellLaunch shell,
        CancellationToken cancellationToken)
    {
        long deadline = Environment.TickCount64 + (long)WindowTimeout.TotalMilliseconds;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (shell.HasExited)
            {
                return false;
            }

            if (tryClose())
            {
                return true;
            }

            if (Environment.TickCount64 >= deadline)
            {
                return false;
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<NativeStartupResult> WaitForCleanExitAsync(ShellLaunch shell, string closeAction, string displayLabel)
    {
        bool exited = await shell.WaitForExitAsync(ExitTimeout, CancellationToken.None).ConfigureAwait(false);
        if (!exited)
        {
            string output = await shell.StopAndReadOutputAsync().ConfigureAwait(false);
            return WithHost(NativeStartupResult.Fail(
                null,
                $"The check sent {closeAction}. The shell was still running after {ExitTimeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} seconds. {output}"));
        }

        if (shell.ExitCode != 0)
        {
            string output = await shell.StopAndReadOutputAsync().ConfigureAwait(false);
            return WithHost(NativeStartupResult.Fail(
                shell.ExitCode,
                $"The check sent {closeAction}. The shell exited with code {shell.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}. {output}"));
        }

        return WithHost(NativeStartupResult.Pass(
            $"PASS. The shell opened a native window. The check sent {closeAction}. The shell exited with code 0. Display: {displayLabel}."));
    }

    private static async Task<NativeStartupResult> WindowDidNotCloseAsync(ShellLaunch shell, string closeAction, string? probeDetail)
    {
        bool exited = shell.HasExited;
        int? exitCode = exited ? shell.ExitCode : null;
        string output = await shell.StopAndReadOutputAsync().ConfigureAwait(false);
        string state = exited
            ? $"The shell exited with code {shell.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)} before a native window accepted {closeAction}."
            : $"No native window accepted {closeAction} within {WindowTimeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} seconds.";
        string probe = string.IsNullOrWhiteSpace(probeDetail) ? string.Empty : " Probe: " + probeDetail;
        return WithHost(NativeStartupResult.Fail(exitCode, $"{state} {output}{probe}"));
    }

    private static NativeStartupResult WithHost(NativeStartupResult result)
    {
        return result.Append(DescribeHost());
    }

    private static string DescribeHost()
    {
        string operatingSystem = ReadLinuxPrettyName() ?? RuntimeInformation.OSDescription;
        return $"Host: {operatingSystem}. Architecture: {RuntimeInformation.OSArchitecture}. Runtime: {RuntimeInformation.FrameworkDescription}.";
    }

    private static string? ReadLinuxPrettyName()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/etc/os-release"))
        {
            return null;
        }

        foreach (string line in File.ReadLines("/etc/os-release"))
        {
            const string prefix = "PRETTY_NAME=";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            return line[prefix.Length..].Trim().Trim('"');
        }

        return null;
    }

    private static string ResolveAppDirectory()
    {
        string root = FindRepositoryRoot();
        string configuration = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
            ?? "Release";
        return Path.Combine(root, "src", "Editor.App", "bin", configuration, "net10.0");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tangopad.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Cannot find Tangopad.slnx above the test assembly.");
    }
}

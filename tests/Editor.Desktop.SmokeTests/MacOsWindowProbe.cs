using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

namespace Editor.Desktop.SmokeTests;

[SupportedOSPlatform("macos")]
internal static class MacOsWindowProbe
{
    private const string Script = """
        on run argv
          set targetPid to (item 1 of argv) as integer
          tell application "System Events"
            set matches to every process whose unix id is targetPid
            if (count of matches) is 0 then
              return "NO_PROCESS"
            end if
            set targetProcess to item 1 of matches
            if (count of windows of targetProcess) is 0 then
              return "NO_WINDOW"
            end if
            set frontmost of targetProcess to true
            tell targetProcess to click (first button of window 1 whose subrole is "AXCloseButton")
            return "CLOSED"
          end tell
        end run
        """;

    internal static async Task<string?> TrySendCloseAsync(int processId, CancellationToken cancellationToken)
    {
        if (!File.Exists("/usr/bin/osascript"))
        {
            return "osascript is not installed.";
        }

        string scriptPath = Path.Combine(Path.GetTempPath(), "tangopad-native-startup-" + processId.ToString(CultureInfo.InvariantCulture) + ".applescript");
        await File.WriteAllTextAsync(scriptPath, Script, cancellationToken).ConfigureAwait(false);
        try
        {
            ProcessStartInfo startInfo = new("/usr/bin/osascript")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));
            using Process process = new() { StartInfo = startInfo };
            if (!process.Start())
            {
                return "osascript did not start.";
            }

            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                return "osascript timed out.";
            }

            string output = (await standardOutput.ConfigureAwait(false)).Trim();
            string error = (await standardError.ConfigureAwait(false)).Trim();
            return process.ExitCode == 0 && output == "CLOSED"
                ? null
                : error.Length == 0 ? output.Length == 0 ? "osascript returned no result." : output : error;
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }
}

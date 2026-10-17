using System.Diagnostics;
using System.Text;

namespace Editor.Desktop.SmokeTests;

internal sealed class ShellLaunch : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _standardOutput;
    private readonly Task<string> _standardError;

    private ShellLaunch(Process process)
    {
        _process = process;
        _standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        _standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
    }

    internal int Id => _process.Id;

    internal bool HasExited => _process.HasExited;

    internal int ExitCode => _process.ExitCode;

    internal static ShellLaunch Start(string executable, string workingDirectory, string? displayName)
    {
        ProcessStartInfo startInfo = new(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (displayName is not null)
        {
            startInfo.Environment["DISPLAY"] = displayName;
        }

        Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("The shell process did not start.");
        }

        return new ShellLaunch(process);
    }

    internal async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await _process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    internal async Task<string> StopAndReadOutputAsync()
    {
        if (!_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between the check and Kill.
            }
        }

        await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        string standardOutput = await _standardOutput.ConfigureAwait(false);
        string standardError = await _standardError.ConfigureAwait(false);
        StringBuilder output = new();
        if (!string.IsNullOrWhiteSpace(standardOutput))
        {
            _ = output.Append("stdout: ");
            _ = output.Append(Trim(standardOutput));
        }

        if (!string.IsNullOrWhiteSpace(standardError))
        {
            if (output.Length > 0)
            {
                _ = output.Append(' ');
            }

            _ = output.Append("stderr: ");
            _ = output.Append(Trim(standardError));
        }

        return output.Length == 0 ? "The shell wrote no output." : output.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between the check and Kill.
            }

            await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        _ = await Task.WhenAll(_standardOutput, _standardError).ConfigureAwait(false);
        _process.Dispose();
    }

    private static string Trim(string value)
    {
        const int limit = 2000;
        string trimmed = value.Trim();
        return trimmed.Length <= limit ? trimmed : trimmed[^limit..];
    }
}

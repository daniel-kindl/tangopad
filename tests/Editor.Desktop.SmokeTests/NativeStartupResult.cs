namespace Editor.Desktop.SmokeTests;

internal enum NativeStartupStatus
{
    Passed,
    NotTested,
    Failed,
}

internal sealed class NativeStartupResult
{
    private NativeStartupResult(NativeStartupStatus status, int? exitCode, string detail)
    {
        Status = status;
        ExitCode = exitCode;
        Detail = detail;
    }

    internal NativeStartupStatus Status { get; }

    internal int? ExitCode { get; }

    internal string Detail { get; }

    internal static NativeStartupResult Pass(string detail)
    {
        return new NativeStartupResult(NativeStartupStatus.Passed, 0, detail);
    }

    internal static NativeStartupResult NotTested(string reason, string followUp)
    {
        return new NativeStartupResult(
            NativeStartupStatus.NotTested,
            null,
            $"NOT TESTED: {reason} Follow-up: {followUp}");
    }

    internal static NativeStartupResult Fail(int? exitCode, string detail)
    {
        return new NativeStartupResult(NativeStartupStatus.Failed, exitCode, $"FAILED. {detail}");
    }

    internal NativeStartupResult Append(string suffix)
    {
        return new NativeStartupResult(Status, ExitCode, Detail + " " + suffix);
    }
}

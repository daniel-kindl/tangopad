using Tangopad.BuildVerification;

if (args.Length != 1)
{
    await Console.Error.WriteLineAsync("Usage: BuildVerification <repository-root>").ConfigureAwait(false);
    return 1;
}

try
{
    await Reproducibility.VerifyAsync(Path.GetFullPath(args[0])).ConfigureAwait(false);
    return 0;
}
catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
{
    await Console.Error.WriteLineAsync(error.Message).ConfigureAwait(false);
    return 1;
}

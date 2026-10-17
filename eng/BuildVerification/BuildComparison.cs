using System.Security.Cryptography;

namespace Tangopad.BuildVerification;

internal static class BuildComparison
{
    internal static SortedDictionary<string, string> Compare(string left, string right, IReadOnlyList<string> outputs)
    {
        string leftRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        string rightRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (leftRoot.Equals(rightRoot, comparison)
            || leftRoot.StartsWith(rightRoot + Path.DirectorySeparatorChar, comparison)
            || rightRoot.StartsWith(leftRoot + Path.DirectorySeparatorChar, comparison))
        {
            throw new ArgumentException("Build directories must be distinct and must not contain one another.");
        }

        if (outputs.Count == 0)
        {
            throw new ArgumentException("The comparison requires at least one output.", nameof(outputs));
        }

        SortedDictionary<string, string> hashes = new(StringComparer.Ordinal);
        foreach (string output in outputs)
        {
            string leftHash = Hash(OutputPath(leftRoot, output));
            string rightHash = Hash(OutputPath(rightRoot, output));
            if (leftHash != rightHash)
            {
                throw new InvalidDataException($"Managed output differs: {output}. Left SHA-256: {leftHash}. Right SHA-256: {rightHash}.");
            }

            hashes.Add(output, leftHash);
        }

        return hashes;
    }

    private static string OutputPath(string root, string output)
    {
        string path = Path.GetFullPath(Path.Combine(root, output));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Path.IsPathRooted(output) || !path.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            throw new ArgumentException($"The output must be relative to its build directory: {output}");
        }

        string? current = path;
        while (current is not null && current.Length >= root.Length)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Output links are not supported: {current}");
            }

            current = Path.GetDirectoryName(current);
        }

        return path;
    }

    internal static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

namespace Tangopad.BuildVerification;

internal static class SourceSnapshot
{
    internal static SortedDictionary<string, string> Copy(string source, string destination, IEnumerable<string> files)
    {
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException($"The source destination must not exist: {destination}");
        }

        _ = Directory.CreateDirectory(destination);
        SortedDictionary<string, string> inputs = new(StringComparer.Ordinal);
        foreach (string file in files.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (file.Split('/').Any(part => part is "bin" or "obj" or "artifacts" or ".git") || !File.Exists(Path.Combine(source, file)))
            {
                continue;
            }

            string original = Path.Combine(source, file);
            if ((File.GetAttributes(original) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Source links are not supported: {file}");
            }

            string copy = Path.Combine(destination, file);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(original, copy);
            inputs.Add(file, BuildComparison.Hash(copy));
        }

        return inputs;
    }
}

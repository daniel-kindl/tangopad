namespace Editor.Infrastructure;

internal static class UserDataLocation
{
    internal static string? FilePath(string filename)
    {
        string directory = Environment.GetFolderPath(OperatingSystem.IsMacOS()
            ? Environment.SpecialFolder.UserProfile : Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        return Resolve(directory, OperatingSystem.IsMacOS(), filename);
    }

    internal static string? Resolve(string directory, bool macOS, string filename)
    {
        return string.IsNullOrEmpty(directory) ? null : macOS
            ? Path.Combine(directory, "Library", "Application Support", "Tangopad", filename)
            : Path.Combine(directory, "Tangopad", filename);
    }
}

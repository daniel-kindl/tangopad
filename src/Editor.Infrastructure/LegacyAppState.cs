using System.Text.Json;

namespace Editor.Infrastructure;

internal static class LegacyAppState
{
    internal static async Task<BinaryAppState> LoadAsync(string directory)
    {
        EditorPresentation presentation = new();
        string[] paths = [];
        using JsonDocument? preferences = await ReadAsync(Path.Combine(directory, "editor-preferences.json"), 4096).ConfigureAwait(false);
        if (preferences is not null)
        {
            JsonElement root = preferences.RootElement;
            CheckVersion(root);
            bool wrap = ReadBool(root, "wordWrap", false);
            bool numbers = ReadBool(root, "showLineNumbers", true);
            double font = root.TryGetProperty("fontSize", out JsonElement size) && size.ValueKind == JsonValueKind.Number
                && size.TryGetDouble(out double value) && double.IsFinite(value) ? Math.Clamp(value, 8, 48) : 15;
            presentation = new(wrap, numbers, font);
        }

        using JsonDocument? history = await ReadAsync(Path.Combine(directory, "recent-files.json"), 65536).ConfigureAwait(false);
        if (history is not null)
        {
            JsonElement root = history.RootElement;
            CheckVersion(root);
            paths = [.. root.GetProperty("paths").EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!).Where(Path.IsPathFullyQualified).Select(Path.GetFullPath)
                .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Take(10)];
        }

        return new(presentation, paths);
    }

    private static bool ReadBool(JsonElement root, string name, bool fallback)
    {
        return root.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : fallback;
    }

    private static void CheckVersion(JsonElement root)
    {
        if (root.GetProperty("version").GetInt32() != 1) { throw new InvalidDataException("The legacy state version is not supported."); }
    }

    private static async Task<JsonDocument?> ReadAsync(string path, int limit)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            byte[] bytes = new byte[limit + 1];
            int length = await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false).ConfigureAwait(false);
            return length > limit
                ? throw new InvalidDataException("The legacy state exceeds its size limit.")
                : JsonDocument.Parse(bytes.AsMemory(0, length));
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}

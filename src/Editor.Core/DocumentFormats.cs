namespace Editor.Core;

/// <summary>Owns filename-based format identification without file access.</summary>
public static class DocumentFormats
{
    /// <summary>Identifies a format from the final filename extension, ignoring case.</summary>
    /// <param name="path">A filename or path, or null for an untitled document.</param>
    /// <returns>Markdown for .md and .markdown. Plain text for .txt and all other names.</returns>
    /// <remarks>
    /// Both slash characters separate path components on every host.
    /// A leading dot alone does not introduce an extension. No path normalization or content validation occurs.
    /// Missing names, unknown extensions, extensionless names, and trailing dots default to plain text.
    /// </remarks>
    public static DocumentFormat Identify(string? path)
    {
        string filename = path ?? string.Empty;
        filename = filename[(Math.Max(filename.LastIndexOf('/'), filename.LastIndexOf('\\')) + 1)..];
        int dot = filename.LastIndexOf('.');
        if (dot <= 0)
        {
            return DocumentFormat.PlainText;
        }

        string extension = filename[dot..];
        return string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".markdown", StringComparison.OrdinalIgnoreCase)
            ? DocumentFormat.Markdown
            : DocumentFormat.PlainText;
    }
}

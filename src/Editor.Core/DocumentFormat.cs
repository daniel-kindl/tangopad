namespace Editor.Core;

/// <summary>Identifies the document's text format without validating its contents.</summary>
public enum DocumentFormat
{
    /// <summary>Plain text, including names without a recognized extension.</summary>
    PlainText,

    /// <summary>Markdown source text.</summary>
    Markdown,
}

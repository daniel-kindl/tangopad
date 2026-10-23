namespace Editor.Core;

/// <summary>Identifies the document's text format without validating its contents.</summary>
public enum DocumentFormat
{
    /// <summary>Plain text, including names without a recognized extension.</summary>
    PlainText,

    /// <summary>Markdown source text.</summary>
    Markdown,

    /// <summary>Redmine Textile source text.</summary>
    Textile,

    /// <summary>JSON source text without content validation.</summary>
    Json,

    /// <summary>XML source text without content validation.</summary>
    Xml,

    /// <summary>XAML source text with general XML highlighting.</summary>
    Xaml,
}

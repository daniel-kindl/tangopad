namespace Editor.Core;

/// <summary>Owns plain text and its last acknowledged clean baseline.</summary>
/// <remarks>
/// Identity belongs to this open document, independently of its text or any path.
/// Instances require serialized access. Acknowledgement does not perform I/O.
/// </remarks>
public sealed class TextDocument
{
    private string baseline;

    private TextDocument(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Id = Guid.NewGuid();
        Text = text;
        baseline = text;
    }

    /// <summary>Gets the stable identity of this open document.</summary>
    public Guid Id { get; }

    /// <summary>Gets the exact text, without normalization.</summary>
    public string Text { get; private set; }

    /// <summary>Gets whether the text differs ordinally from the clean baseline.</summary>
    public bool IsDirty => !string.Equals(Text, baseline, StringComparison.Ordinal);

    /// <summary>Creates an empty, clean document with a new identity.</summary>
    /// <returns>The untitled document.</returns>
    public static TextDocument CreateUntitled()
    {
        return new(string.Empty);
    }

    /// <summary>Creates a document with supplied text as its clean baseline and a new identity.</summary>
    /// <param name="text">The exact initial text. Empty text is valid.</param>
    /// <returns>The clean document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static TextDocument FromText(string text)
    {
        return new(text);
    }

    /// <summary>Replaces the text without changing identity or the clean baseline.</summary>
    /// <param name="text">The exact replacement text. Empty text is valid.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public void ReplaceText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    /// <summary>Sets the current text as the clean baseline without changing identity or text.</summary>
    /// <remarks>
    /// Call only when the current text can be acknowledged as clean.
    /// This method does not save text or acknowledge an older asynchronous save snapshot.
    /// </remarks>
    public void AcceptChanges()
    {
        baseline = Text;
    }
}

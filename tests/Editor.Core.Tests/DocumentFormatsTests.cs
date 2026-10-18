using Xunit;

namespace Editor.Core.Tests;

/// <summary>Checks format identification without filesystem or content access.</summary>
public sealed class DocumentFormatsTests
{
    /// <summary>Checks extensions, path components, and fallback names.</summary>
    /// <param name="path">The filename or path to classify.</param>
    /// <param name="expected">The expected format.</param>
    [Theory]
    [InlineData(null, DocumentFormat.PlainText)]
    [InlineData("", DocumentFormat.PlainText)]
    [InlineData("notes.txt", DocumentFormat.PlainText)]
    [InlineData("notes.TxT", DocumentFormat.PlainText)]
    [InlineData("notes.md", DocumentFormat.Markdown)]
    [InlineData("notes.Md", DocumentFormat.Markdown)]
    [InlineData("notes.markdown", DocumentFormat.Markdown)]
    [InlineData("notes.MARKDOWN", DocumentFormat.Markdown)]
    [InlineData("notes", DocumentFormat.PlainText)]
    [InlineData("notes.rst", DocumentFormat.PlainText)]
    [InlineData("notes.md.txt", DocumentFormat.PlainText)]
    [InlineData("notes.txt.md", DocumentFormat.Markdown)]
    [InlineData("folder.md/notes", DocumentFormat.PlainText)]
    [InlineData("folder.md\\notes", DocumentFormat.PlainText)]
    [InlineData("folder.txt/notes.MD", DocumentFormat.Markdown)]
    [InlineData("C:\\folder.txt\\notes.MARKDOWN", DocumentFormat.Markdown)]
    [InlineData("folder.md/", DocumentFormat.PlainText)]
    [InlineData("folder.md\\", DocumentFormat.PlainText)]
    [InlineData(".md", DocumentFormat.PlainText)]
    [InlineData(".markdown", DocumentFormat.PlainText)]
    [InlineData(".notes.md", DocumentFormat.Markdown)]
    [InlineData("folder/.md", DocumentFormat.PlainText)]
    [InlineData("notes.md.", DocumentFormat.PlainText)]
    [InlineData(".", DocumentFormat.PlainText)]
    [InlineData("..", DocumentFormat.PlainText)]
    [InlineData("poznámky.漢字/🐈.mD", DocumentFormat.Markdown)]
    [InlineData("notes.md ", DocumentFormat.PlainText)]
    public void FilenameDeterminesFormat(string? path, DocumentFormat expected)
    {
        Assert.Equal(expected, DocumentFormats.Identify(path));
    }
}

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
    [InlineData("notes.textile", DocumentFormat.Textile)]
    [InlineData("notes.TEXTILE", DocumentFormat.Textile)]
    [InlineData("notes.md.textile", DocumentFormat.Textile)]
    [InlineData("notes.textile.md", DocumentFormat.Markdown)]
    [InlineData("notes.wiki", DocumentFormat.PlainText)]
    [InlineData("notes.WIKI", DocumentFormat.PlainText)]
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
    [InlineData(".textile", DocumentFormat.PlainText)]
    [InlineData(".wiki", DocumentFormat.PlainText)]
    [InlineData(".notes.md", DocumentFormat.Markdown)]
    [InlineData(".notes.textile", DocumentFormat.Textile)]
    [InlineData("folder/.md", DocumentFormat.PlainText)]
    [InlineData("notes.md.", DocumentFormat.PlainText)]
    [InlineData(".", DocumentFormat.PlainText)]
    [InlineData("..", DocumentFormat.PlainText)]
    [InlineData("poznámky.漢字/🐈.mD", DocumentFormat.Markdown)]
    [InlineData("poznámky.漢字/🐈.TeXtiLe", DocumentFormat.Textile)]
    [InlineData("folder.textile/notes.wiki", DocumentFormat.PlainText)]
    [InlineData("folder.wiki\\notes.textile", DocumentFormat.Textile)]
    [InlineData("notes.md ", DocumentFormat.PlainText)]
    [InlineData("notes.textile ", DocumentFormat.PlainText)]
    public void FilenameDeterminesFormat(string? path, DocumentFormat expected)
    {
        Assert.Equal(expected, DocumentFormats.Identify(path));
    }
    /// <summary>Checks the first structured-text formats and their explicit extension boundaries.</summary>
    /// <param name="path">The filename or path to classify.</param>
    /// <param name="expected">The expected format name.</param>
    [Theory]
    [InlineData("data.json", "Json")]
    [InlineData("C:\\folder.xml\\data.JSON", "Json")]
    [InlineData("folder.json/document.Xml", "Xml")]
    [InlineData("view.XAML", "Xaml")]
    [InlineData(".data.json", "Json")]
    [InlineData("data.xml.json", "Json")]
    [InlineData("data.json.xml", "Xml")]
    [InlineData(".json", "PlainText")]
    [InlineData(".xml", "PlainText")]
    [InlineData(".xaml", "PlainText")]
    [InlineData("data.json ", "PlainText")]
    [InlineData("data.xml.", "PlainText")]
    [InlineData("folder.json/", "PlainText")]
    [InlineData("data.jsonc", "PlainText")]
    [InlineData("view.axaml", "PlainText")]
    [InlineData("data.yaml", "Yaml")]
    [InlineData("data.csv", "Csv")]
    [InlineData("project.csproj", "PlainText")]
    [InlineData("data.YML", "Yaml")]
    [InlineData("C:\\folder.csv\\data.YaMl", "Yaml")]
    [InlineData("folder.yaml/data.TSV", "Tsv")]
    [InlineData("data.csv.tsv", "Tsv")]
    [InlineData("data.tsv.csv", "Csv")]
    [InlineData(".yaml", "PlainText")]
    [InlineData(".yml", "PlainText")]
    [InlineData(".csv", "PlainText")]
    [InlineData(".tsv", "PlainText")]
    [InlineData("data.yml ", "PlainText")]
    [InlineData("data.csv.", "PlainText")]
    [InlineData("folder.tsv/", "PlainText")]
    [InlineData("data.yaml.txt", "PlainText")]
    [InlineData("data.tab", "PlainText")]
    public void StructuredTextExtensionsAreExplicit(string path, string expected)
    {
        Assert.Equal(expected, DocumentFormats.Identify(path).ToString());
    }

}

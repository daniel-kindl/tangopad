using System.Xml.Linq;
using Xunit;

namespace Editor.Architecture.Tests;

/// <summary>Checks resource use in presentation markup.</summary>
public sealed class AxamlResourceTests
{
    private static readonly HashSet<string> ResourceProperties = new(StringComparer.Ordinal)
    {
        "Content", "Text", "Title", "ToolTip.Tip", "Width", "Height", "MinWidth", "MinHeight",
        "MaxHeight", "Watermark", "AutomationProperties.Name", "Spacing", "Margin", "Padding", "FontFamily", "FontSize", "CornerRadius", "Background",
        "Foreground", "BorderBrush", "SelectionBrush", "LineNumbersForeground",
    };

    private static readonly HashSet<string> ThemeProperties = new(StringComparer.Ordinal)
    {
        "Background", "Foreground", "BorderBrush", "SelectionBrush", "LineNumbersForeground",
    };

    /// <summary>Views and styles use resources or bindings for text and reusable visual values.</summary>
    [Fact]
    public void MarkupUsesNamedResources()
    {
        foreach (string file in MarkupFiles())
        {
            Assert.True(Violations(XDocument.Load(file)).Count == 0, $"Use a resource or binding in {file}.");
        }
    }

    /// <summary>The markup check rejects literals and static theme brushes.</summary>
    /// <param name="markup">The invalid markup.</param>
    /// <param name="property">The property that must fail.</param>
    [Theory]
    [InlineData("<Button Content='Save' />", "Content")]
    [InlineData("<TextBox Watermark='Find' />", "Watermark")]
    [InlineData("<TextBox AutomationProperties.Name='Find' />", "AutomationProperties.Name")]
    [InlineData("<TextBox MaxHeight='72' />", "MaxHeight")]
    [InlineData("<Setter Property='Spacing' Value='8' />", "Spacing")]
    [InlineData("<Window Background='{StaticResource Brush.Background}' />", "Background")]
    public void InvalidMarkupIsRejected(string markup, string property)
    {
        Assert.Equal([property], Violations(XDocument.Parse(markup)));
    }

    /// <summary>Resource keys are unique, and both theme dictionaries define the same roles.</summary>
    [Fact]
    public void ResourceKeysAreUniqueWithinTheirScope()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        string[] files = MarkupFiles();
        string[] shared = [.. files.Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => XDocument.Load(file).Descendants().Attributes(xaml + "Key").Select(key => key.Value))];
        Assert.Equal(shared.Length, shared.Distinct(StringComparer.Ordinal).Count());
        string[][] themes = [.. files.Where(file => file.Contains($"{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(file => XDocument.Load(file).Descendants().Attributes(xaml + "Key").Select(key => key.Value).Order(StringComparer.Ordinal).ToArray())];
        Assert.Equal(2, themes.Length);
        Assert.Equal(themes[0], themes[1]);
        Assert.Equal(themes[0].Length, themes[0].Distinct(StringComparer.Ordinal).Count());
    }

    private static List<string> Violations(XDocument document)
    {
        List<string> violations = [];
        foreach (XElement element in document.Descendants())
        {
            foreach (XAttribute attribute in element.Attributes().Where(attribute => ResourceProperties.Contains(attribute.Name.LocalName)))
            {
                Check(attribute.Name.LocalName, attribute.Value);
            }

            if (element.Name.LocalName == "Setter" && element.Attribute("Property") is { } property
                && ResourceProperties.Contains(property.Value) && element.Attribute("Value") is { } value)
            {
                Check(property.Value, value.Value);
            }
        }

        return violations;

        void Check(string property, string value)
        {
            bool binding = value.StartsWith("{Binding ", StringComparison.Ordinal) || value.StartsWith("{CompiledBinding ", StringComparison.Ordinal);
            bool resource = value.StartsWith("{DynamicResource ", StringComparison.Ordinal)
                || (!ThemeProperties.Contains(property) && value.StartsWith("{StaticResource ", StringComparison.Ordinal));
            if (!binding && !resource)
            {
                violations.Add(property);
            }
        }
    }

    private static string[] MarkupFiles()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tangopad.slnx")))
        {
            directory = directory.Parent;
        }

        string root = directory?.FullName ?? throw new DirectoryNotFoundException("Cannot find Tangopad.slnx above the test assembly.");
        return Directory.GetFiles(Path.Combine(root, "src", "Editor.App"), "*.axaml", SearchOption.AllDirectories);
    }
}

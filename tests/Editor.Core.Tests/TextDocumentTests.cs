using Xunit;

namespace Editor.Core.Tests;

/// <summary>Checks document identity, exact text, and baseline behavior.</summary>
public sealed class TextDocumentTests
{
    /// <summary>Checks that acknowledgement preserves edits made after a save starts.</summary>
    [Fact]
    public void AcknowledgementUsesTheSavedSnapshot()
    {
        TextDocument document = TextDocument.FromText("loaded");
        document.ReplaceText("saved snapshot");
        string snapshot = document.Text;
        document.ReplaceText("newer edit");

        document.AcceptChanges(snapshot);

        Assert.Equal("newer edit", document.Text);
        Assert.True(document.IsDirty);
        document.ReplaceText(snapshot);
        Assert.False(document.IsDirty);
    }

    /// <summary>Checks that a new untitled document is empty and clean.</summary>
    [Fact]
    public void NewDocumentIsEmptyAndClean()
    {
        TextDocument document = TextDocument.CreateUntitled();

        Assert.NotEqual(Guid.Empty, document.Id);
        Assert.Equal(string.Empty, document.Text);
        Assert.False(document.IsDirty);
    }

    /// <summary>Checks that each open document gets its own identity.</summary>
    [Fact]
    public void SeparateDocumentsHaveDifferentIdentities()
    {
        TextDocument first = TextDocument.CreateUntitled();
        TextDocument second = TextDocument.CreateUntitled();
        TextDocument loaded = TextDocument.FromText(string.Empty);
        TextDocument loadedAgain = TextDocument.FromText(string.Empty);

        Assert.Equal(4, new[] { first.Id, second.Id, loaded.Id, loadedAgain.Id }.Distinct().Count());
        Assert.NotEqual(Guid.Empty, loaded.Id);
    }

    /// <summary>Checks supplied text without normalization or loss.</summary>
    /// <param name="text">The supplied document text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("Příliš žluťoučký 🐈\r\n漢字\nالعربية\t\0e\u0301")]
    public void SuppliedTextIsTheCleanBaseline(string text)
    {
        TextDocument document = TextDocument.FromText(text);

        Assert.Equal(text, document.Text);
        Assert.False(document.IsDirty);
    }

    /// <summary>Checks repeated edits, unchanged edits, identity, and baseline reversion.</summary>
    /// <param name="baseline">The initial text.</param>
    /// <param name="edited">The replacement text.</param>
    [Theory]
    [InlineData("", "🐈\r\n漢字")]
    [InlineData("original", "")]
    [InlineData("text", "TEXT")]
    [InlineData("é", "e\u0301")]
    public void DirtyStateTracksExactTextChanges(string baseline, string edited)
    {
        TextDocument document = TextDocument.FromText(baseline);
        Guid identity = document.Id;

        document.ReplaceText(baseline);
        Assert.False(document.IsDirty);
        document.ReplaceText(edited);
        Assert.Equal(edited, document.Text);
        Assert.True(document.IsDirty);
        document.ReplaceText(edited);
        Assert.True(document.IsDirty);
        document.ReplaceText("another edit");
        Assert.True(document.IsDirty);
        document.ReplaceText(baseline);
        Assert.Equal(baseline, document.Text);
        Assert.False(document.IsDirty);
        Assert.Equal(identity, document.Id);
    }

    /// <summary>Checks edits and reversion for an untitled document.</summary>
    [Fact]
    public void UntitledDocumentReturnsToCleanWhenEmptied()
    {
        TextDocument document = TextDocument.CreateUntitled();
        document.ReplaceText("draft");
        Assert.True(document.IsDirty);

        document.ReplaceText(string.Empty);
        Assert.False(document.IsDirty);
    }

    /// <summary>Checks that acknowledgement moves the baseline without changing identity or text.</summary>
    [Fact]
    public void AcknowledgementReplacesTheBaseline()
    {
        TextDocument document = TextDocument.FromText("loaded");
        Guid identity = document.Id;
        document.ReplaceText("saved");

        document.AcceptChanges(document.Text);
        document.AcceptChanges(document.Text);
        Assert.False(document.IsDirty);
        Assert.Equal("saved", document.Text);
        Assert.Equal(identity, document.Id);

        document.ReplaceText("loaded");
        Assert.True(document.IsDirty);
        document.ReplaceText("saved");
        Assert.False(document.IsDirty);
    }

    /// <summary>Checks that an empty replacement can become the clean baseline.</summary>
    [Fact]
    public void EmptyTextCanBeAcknowledged()
    {
        TextDocument document = TextDocument.FromText("loaded");
        document.ReplaceText(string.Empty);
        document.AcceptChanges(document.Text);

        Assert.Equal(string.Empty, document.Text);
        Assert.False(document.IsDirty);
        document.ReplaceText("loaded");
        Assert.True(document.IsDirty);
        document.ReplaceText(string.Empty);
        Assert.False(document.IsDirty);
    }

    /// <summary>Checks that null input cannot create a document.</summary>
    [Fact]
    public void NullSuppliedTextIsRejected()
    {
        ArgumentNullException error = Assert.Throws<ArgumentNullException>(() => TextDocument.FromText(null!));
        Assert.Equal("text", error.ParamName);
    }

    /// <summary>Checks that null edits preserve text, identity, and the baseline.</summary>
    [Fact]
    public void NullEditLeavesTheDocumentUnchanged()
    {
        TextDocument document = TextDocument.FromText("loaded");
        Guid identity = document.Id;
        document.ReplaceText("draft");

        ArgumentNullException error = Assert.Throws<ArgumentNullException>(() => document.ReplaceText(null!));

        Assert.Equal("text", error.ParamName);
        Assert.Equal("draft", document.Text);
        Assert.Equal(identity, document.Id);
        Assert.True(document.IsDirty);
        document.ReplaceText("loaded");
        Assert.False(document.IsDirty);
    }

    /// <summary>Checks that null acknowledgement preserves the previous baseline.</summary>
    [Fact]
    public void NullAcknowledgementLeavesTheDocumentUnchanged()
    {
        TextDocument document = TextDocument.FromText("loaded");
        document.ReplaceText("draft");

        ArgumentNullException error = Assert.Throws<ArgumentNullException>(() => document.AcceptChanges(null!));

        Assert.Equal("text", error.ParamName);
        Assert.Equal("draft", document.Text);
        Assert.True(document.IsDirty);
        document.ReplaceText("loaded");
        Assert.False(document.IsDirty);
    }
}

namespace Editor.Preview;

/// <summary>Inert preview text. No node can load a resource or invoke an action.</summary>
/// <param name="Blocks">The bounded sequence of display blocks.</param>
/// <param name="IsLimited">Whether a resource limit prevented a complete preview.</param>
public sealed record PreviewDocument(IReadOnlyList<PreviewBlock> Blocks, bool IsLimited);

/// <summary>A native preview block with no executable behavior.</summary>
public abstract record PreviewBlock;

/// <summary>A flat display block with text-only list and quote markers.</summary>
/// <param name="Runs">The styled text runs.</param>
/// <param name="Prefix">The list or quote marker.</param>
/// <param name="Depth">The indentation level.</param>
/// <param name="HeadingLevel">The heading level, or zero for body text.</param>
/// <param name="Code">Whether this is a code block.</param>
/// <param name="ThematicBreak">Whether this is a thematic break.</param>
public sealed record PreviewTextBlock(IReadOnlyList<PreviewRun> Runs, string Prefix = "", int Depth = 0,
    int HeadingLevel = 0, bool Code = false, bool ThematicBreak = false) : PreviewBlock;

/// <summary>A text run with semantic presentation flags and no resource destination.</summary>
/// <param name="Text">The literal display text.</param>
/// <param name="Strong">Whether to use strong emphasis.</param>
/// <param name="Emphasis">Whether to use emphasis.</param>
/// <param name="Code">Whether to use code typography.</param>
/// <param name="Link">Whether this is an inactive link label.</param>
/// <param name="Underline">Whether to underline the text.</param>
/// <param name="Strike">Whether to strike through the text.</param>
public sealed record PreviewRun(string Text, bool Strong = false, bool Emphasis = false, bool Code = false,
    bool Link = false, bool Underline = false, bool Strike = false);

/// <summary>A table containing native text cells without resource destinations.</summary>
/// <param name="Rows">The bounded sequence of table rows.</param>
public sealed record PreviewTableBlock(IReadOnlyList<PreviewTableRow> Rows) : PreviewBlock;

/// <summary>One table row.</summary>
/// <param name="Cells">The cells in source order.</param>
public sealed record PreviewTableRow(IReadOnlyList<PreviewTableCell> Cells);

/// <summary>A table cell containing inert styled text.</summary>
/// <param name="Runs">The cell's text runs.</param>
/// <param name="Heading">Whether the cell is a heading.</param>
public sealed record PreviewTableCell(IReadOnlyList<PreviewRun> Runs, bool Heading = false);

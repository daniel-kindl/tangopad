namespace Editor.Formats.Markdown;

/// <summary>Inert preview text. No node can load a resource or invoke an action.</summary>
/// <param name="Blocks">The bounded sequence of display blocks.</param>
/// <param name="IsLimited">Whether a resource limit prevented a complete preview.</param>
public sealed record PreviewDocument(IReadOnlyList<PreviewBlock> Blocks, bool IsLimited);

/// <summary>A flat display block with text-only list and quote markers.</summary>
/// <param name="Runs">The styled text runs.</param>
/// <param name="Prefix">The list or quote marker.</param>
/// <param name="Depth">The indentation level.</param>
/// <param name="HeadingLevel">The heading level, or zero for body text.</param>
/// <param name="Code">Whether this is a code block.</param>
/// <param name="ThematicBreak">Whether this is a thematic break.</param>
public sealed record PreviewBlock(IReadOnlyList<PreviewRun> Runs, string Prefix = "", int Depth = 0,
    int HeadingLevel = 0, bool Code = false, bool ThematicBreak = false);

/// <summary>A text run with semantic presentation flags and no resource destination.</summary>
/// <param name="Text">The literal display text.</param>
/// <param name="Strong">Whether to use strong emphasis.</param>
/// <param name="Emphasis">Whether to use emphasis.</param>
/// <param name="Code">Whether to use code typography.</param>
/// <param name="Link">Whether this is an inactive link label.</param>
public sealed record PreviewRun(string Text, bool Strong = false, bool Emphasis = false, bool Code = false, bool Link = false);

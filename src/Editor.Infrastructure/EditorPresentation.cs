namespace Editor.Infrastructure;

/// <summary>Contains the editor preferences, independently of document state.</summary>
public sealed record EditorPresentation(bool WordWrap = false, bool ShowLineNumbers = true, double FontSize = 15)
{
    /// <summary>Gets the minimum editor font size.</summary>
    public const double MinimumFontSize = 8;

    /// <summary>Gets the maximum editor font size.</summary>
    public const double MaximumFontSize = 48;

    /// <summary>Gets the default editor font size.</summary>
    public const double DefaultFontSize = 15;
}

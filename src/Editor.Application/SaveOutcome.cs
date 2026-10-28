namespace Editor.Application;

/// <summary>Contains the outcome of a document save.</summary>
/// <param name="saved">False for a conflict without a write.</param>
/// <param name="flushed">False when the file was written but could not be flushed to the disk. The document then stays modified.</param>
public sealed class SaveOutcome(bool saved, bool flushed)
{
    /// <summary>Gets a value indicating whether the snapshot was written.</summary>
    public bool Saved { get; } = saved;

    /// <summary>Gets a value indicating whether the written snapshot was flushed to the disk.</summary>
    public bool Flushed { get; } = flushed;
}

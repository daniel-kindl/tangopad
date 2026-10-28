namespace Editor.Infrastructure;

/// <summary>Flushes saved data to the disk.</summary>
internal interface IFileSync
{
    /// <summary>Flushes the data of an open file to the disk.</summary>
    /// <param name="stream">The open file.</param>
    void FlushFile(FileStream stream);

    /// <summary>Flushes the entries of a folder to the disk.</summary>
    /// <param name="path">The full path of the folder.</param>
    void FlushDirectory(string path);
}

/// <summary>Flushes files and folders with the operating system.</summary>
internal sealed class FileSync : IFileSync
{
    /// <summary>Gets the shared instance.</summary>
    public static FileSync Default { get; } = new();

    /// <inheritdoc />
    public void FlushFile(FileStream stream)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public void FlushDirectory(string path)
    {
        throw new NotImplementedException();
    }
}

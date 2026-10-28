using System.Runtime.InteropServices;

namespace Editor.Infrastructure;

/// <summary>Flushes saved data to the disk.</summary>
internal interface IFileSync
{
    /// <summary>Flushes the data of an open file to the disk.</summary>
    /// <param name="stream">The open file.</param>
    void FlushFile(FileStream stream);

    /// <summary>Flushes the entries of a folder to the disk.</summary>
    /// <param name="path">The full path of the folder.</param>
    /// <returns>True after the flush, or false when this system has no known way to flush the folder.</returns>
    /// <exception cref="IOException">The folder could not be opened or flushed.</exception>
    bool FlushDirectory(string path);
}

/// <summary>Flushes files and folders with the operating system.</summary>
/// <remarks>Windows has no folder flush, so <see cref="FlushDirectory"/> does nothing there.</remarks>
internal sealed partial class FileSync : IFileSync
{
    private const int ReadOnly = 0;

    // The flag values differ between the systems: see fcntl.h on Linux (x64 and arm64) and on macOS.
    private const int LinuxDirectory = 0x0001_0000;
    private const int LinuxCloseOnExec = 0x0008_0000;
    private const int MacDirectory = 0x0010_0000;
    private const int MacCloseOnExec = 0x0100_0000;

    private readonly int? directoryFlags;

    internal FileSync(int? directoryFlags)
    {
        this.directoryFlags = directoryFlags;
    }

    /// <summary>Gets the shared instance.</summary>
    public static FileSync Default { get; } = new(DirectoryOpenFlags(OperatingSystem.IsMacOS(), RuntimeInformation.ProcessArchitecture));

    /// <summary>Selects the flags for the folder open: read only, folders only and close on exec.</summary>
    /// <param name="isMacOS">Whether the system is macOS. Otherwise it is Linux.</param>
    /// <param name="architecture">The process architecture.</param>
    /// <returns>The flags, or null when the values for this system are not known.</returns>
    internal static int? DirectoryOpenFlags(bool isMacOS, Architecture architecture)
    {
        _ = architecture;
        return isMacOS ? ReadOnly | MacDirectory | MacCloseOnExec : ReadOnly | LinuxDirectory | LinuxCloseOnExec;
    }

    /// <inheritdoc />
    public void FlushFile(FileStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Flush(true) calls fsync on Unix and FlushFileBuffers on Windows. No test can observe that call.
        stream.Flush(flushToDisk: true);
    }

    /// <inheritdoc />
    public bool FlushDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        int descriptor = NativeMethods.Open(path, directoryFlags ?? (ReadOnly | LinuxDirectory | LinuxCloseOnExec));
        if (descriptor < 0)
        {
            throw new IOException($"The folder '{path}' could not be opened to flush it (error {Marshal.GetLastPInvokeError()}).");
        }

        try
        {
            if (NativeMethods.Fsync(descriptor) != 0)
            {
                throw new IOException($"The folder '{path}' could not be flushed (error {Marshal.GetLastPInvokeError()}).");
            }
        }
        finally
        {
            _ = NativeMethods.Close(descriptor);
        }

        return true;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int Open(string path, int flags);

        [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int Fsync(int descriptor);

        [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int Close(int descriptor);
    }
}

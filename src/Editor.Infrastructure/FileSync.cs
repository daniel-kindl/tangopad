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

    // The flag values differ between the systems and processors: see fcntl.h on Linux and on macOS.
    // Linux x64 uses 0x10000 for O_DIRECTORY. Linux arm64 and 32-bit arm use 0x4000. O_CLOEXEC is 0x80000 on all three.
    private const int LinuxDirectory = 0x0001_0000;
    private const int LinuxArmDirectory = 0x0000_4000;
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
        int? linuxDirectory = architecture switch
        {
            Architecture.X64 => LinuxDirectory,
            Architecture.Arm64 or Architecture.Arm => LinuxArmDirectory,
            Architecture.X86 or Architecture.Wasm or Architecture.S390x or Architecture.LoongArch64
                or Architecture.Armv6 or Architecture.Ppc64le or Architecture.RiscV64 => null,
            _ => null,
        };
        return isMacOS ? ReadOnly | MacDirectory | MacCloseOnExec : ReadOnly | linuxDirectory | LinuxCloseOnExec;
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

        // With unknown flags an open could follow the wrong rules, so skip the flush and report it.
        if (directoryFlags is not int flags)
        {
            return false;
        }

        int descriptor = NativeMethods.Open(path, flags);
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

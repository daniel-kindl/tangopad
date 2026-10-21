using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Editor.Application;

namespace Editor.Infrastructure;

/// <summary>Stores plain text in local UTF-8 files.</summary>
/// <remarks>
/// Loads consume one initial UTF-8 BOM. Saves add no BOM and replace the destination directory entry.
/// Replacement does not preserve file metadata. See ADR 0009 for optimistic concurrency limits.
/// </remarks>
public sealed class FileTextDocumentStore : ITextDocumentStore
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <inheritdoc />
    public async Task<StoredDocument> LoadAsync(string destination, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        cancellationToken.ThrowIfCancellationRequested();
        string path = Path.GetFullPath(destination);
        using FileStream stream = new(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read | FileShare.Delete,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });
        long length = stream.Length;
        DateTime modified = File.GetLastWriteTimeUtc(stream.SafeFileHandle);
        using SHA256 hash = SHA256.Create();
        using CryptoStream hashing = new(stream, hash, CryptoStreamMode.Read);
        using StreamReader reader = new(hashing, StrictUtf8, detectEncodingFromByteOrderMarks: false);
        string text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return stream.Length != length || File.GetLastWriteTimeUtc(stream.SafeFileHandle) != modified
            ? throw new IOException("The file changed during loading. Reload the file.")
            : new(text.StartsWith('\uFEFF') ? text[1..] : text, Revision(path, length, modified, hash.Hash!));
    }

    /// <inheritdoc />
    public async Task<string?> SaveAsync(string destination, string text, string? expectedRevision, bool overwrite, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        string path = Path.GetFullPath(destination);
        string? before = await ReadRevisionAsync(path, overwrite ? null : expectedRevision ?? string.Empty, false, cancellationToken).ConfigureAwait(false);
        if (!overwrite && (expectedRevision is not null && expectedRevision.StartsWith(path + "\n", StringComparison.Ordinal)
            ? !string.Equals(before, expectedRevision, StringComparison.Ordinal)
            : before is not null))
        {
            return null;
        }

        string temporary = Path.Combine(Path.GetDirectoryName(path)!, $".tangopad-{Guid.NewGuid():N}.tmp");
        bool created = false;
        try
        {
            FileStreamOptions options = new()
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (FileStream stream = new(temporary, options))
            {
                created = true;
                using StreamWriter writer = new(stream, StrictUtf8);
                await writer.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            string persisted = await ReadRevisionAsync(temporary, null, false, cancellationToken).ConfigureAwait(false)
                ?? throw new IOException("The temporary file disappeared before saving.");
            persisted = path + persisted[temporary.Length..];
            string? current = await ReadRevisionAsync(path, before ?? string.Empty, false, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(current, before, StringComparison.Ordinal))
            {
                File.Delete(temporary);
                return null;
            }

            // A new destination uses a non-overwriting move, including the final race window.
            try
            {
                File.Move(temporary, path, overwrite: before is not null);
            }
            catch (IOException) when (before is null && File.Exists(path))
            {
                File.Delete(temporary);
                return null;
            }

            return persisted;
        }
        catch (Exception failure)
        {
            if (created)
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException("The save failed. The temporary file could not be removed.", failure, cleanupFailure);
                }
            }

            throw;
        }
    }

    private static string Revision(string path, long length, DateTime modified, byte[] hash)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{path}\n{length}\n{modified.Ticks}\n{Convert.ToHexString(hash)}");
    }

    internal static async Task<string?> ReadRevisionAsync(string path, string? comparison, bool allowConcurrentWrites, CancellationToken token)
    {
        try
        {
            using FileStream stream = new(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = allowConcurrentWrites ? FileShare.ReadWrite | FileShare.Delete : FileShare.Read | FileShare.Delete,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
            long length = stream.Length;
            DateTime modified = File.GetLastWriteTimeUtc(stream.SafeFileHandle);
            string prefix = string.Create(CultureInfo.InvariantCulture, $"{path}\n{length}\n{modified.Ticks}\n");
            if (comparison is not null && !comparison.StartsWith(prefix, StringComparison.Ordinal))
            {
                // A different path, length, or timestamp already proves a conflict.
                return prefix;
            }

            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[64 * 1024];
            long remaining = length;
            while (remaining > 0)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new IOException("The file changed during the save check. Try again.");
                }

                hash.AppendData(buffer, 0, read);
                remaining -= read;
            }

            return stream.Length != length || File.GetLastWriteTimeUtc(stream.SafeFileHandle) != modified
                ? throw new IOException("The file changed during the save check. Try again.")
                : Revision(path, length, modified, hash.GetHashAndReset());
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}

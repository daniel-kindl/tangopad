using System.Text;
using Editor.Application;

namespace Editor.Infrastructure;

/// <summary>Stores plain text in local UTF-8 files.</summary>
/// <remarks>
/// Loads consume one initial UTF-8 BOM. Saves add no BOM and replace the destination directory entry.
/// Replacement does not preserve file metadata or coordinate writers. See ADR 0006 for platform limits.
/// </remarks>
public sealed class FileTextDocumentStore : ITextDocumentStore
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <inheritdoc />
    public async Task<string> LoadAsync(string destination, CancellationToken cancellationToken)
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
        using StreamReader reader = new(stream, StrictUtf8, detectEncodingFromByteOrderMarks: false);
        string text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return text.StartsWith('\uFEFF') ? text[1..] : text;
    }

    /// <inheritdoc />
    public async Task SaveAsync(string destination, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        string path = Path.GetFullPath(destination);
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

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
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
}

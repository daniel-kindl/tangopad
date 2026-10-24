using System.Security.Cryptography;
using System.Text;

namespace Editor.Infrastructure;

internal sealed record BinaryAppState(EditorPresentation Presentation, string[] Paths)
{
    internal const int MaximumSize = 65536;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal byte[] Encode()
    {
        using MemoryStream content = new();
        using (BinaryWriter writer = new(content, Utf8, leaveOpen: true))
        {
            writer.Write("TANGOST\0"u8);
            writer.Write(1u);
            writer.Write(Presentation.WordWrap ? (byte)1 : (byte)0);
            writer.Write(Presentation.ShowLineNumbers ? (byte)1 : (byte)0);
            writer.Write(Presentation.FontSize);
            writer.Write((uint)Paths.Length);
            foreach (string path in Paths)
            {
                byte[] bytes = Utf8.GetBytes(path);
                if (bytes.Length > 4096) { throw new InvalidDataException("A recent path exceeds 4096 UTF-8 bytes."); }
                writer.Write((uint)bytes.Length);
                writer.Write(bytes);
            }
        }

        byte[] payload = content.ToArray();
        return [.. payload, .. SHA256.HashData(payload)];
    }

    internal static BinaryAppState Decode(byte[] bytes)
    {
        if (bytes.Length is < 58 or > MaximumSize || !bytes.AsSpan(0, 8).SequenceEqual("TANGOST\0"u8)
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)), bytes.AsSpan(bytes.Length - 32)))
        {
            throw new InvalidDataException("The state header, length, or checksum is invalid.");
        }

        using MemoryStream content = new(bytes, 0, bytes.Length - 32, writable: false);
        using BinaryReader reader = new(content, Utf8);
        content.Position = 8;
        if (reader.ReadUInt32() != 1) { throw new InvalidDataException("The state schema version is not supported."); }
        byte wrap = reader.ReadByte();
        byte numbers = reader.ReadByte();
        double font = reader.ReadDouble();
        uint count = reader.ReadUInt32();
        if (wrap > 1 || numbers > 1 || !double.IsFinite(font) || font is < 8 or > 48 || count > 10)
        {
            throw new InvalidDataException("The state preferences or path count are invalid.");
        }

        List<string> paths = [];
        for (uint index = 0; index < count; index++)
        {
            uint length = reader.ReadUInt32();
            if (length is 0 or > 4096 || length > content.Length - content.Position)
            {
                throw new InvalidDataException("A state path length is invalid.");
            }

            string path = Utf8.GetString(reader.ReadBytes((int)length));
            if (!Path.IsPathFullyQualified(path) || path.Contains('\0', StringComparison.Ordinal))
            {
                throw new InvalidDataException("A state path is invalid.");
            }
            paths.Add(path);
        }

        return content.Position != content.Length
            ? throw new InvalidDataException("The state contains unexpected bytes.")
            : new(new(wrap == 1, numbers == 1, font), [.. paths]);
    }
}

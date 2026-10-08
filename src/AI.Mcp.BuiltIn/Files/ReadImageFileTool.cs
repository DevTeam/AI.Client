namespace AI.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using AI.Contracts.FileSystem;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>
/// Reads one image file and hands the picture itself to the caller as an image content block.
/// Metadata alone is not the picture: a path, a size and a media type let a model reason about the
/// file, but not look at what is in it, which is exactly what a multimodal model is asked to do.
/// </summary>
[McpServerToolType]
public sealed class ReadImageFileTool(IPathGuard guard, IBuiltInToolReply reply, IFileSystem files) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(
        ReadAsync,
        new McpServerToolCreateOptions
        {
            Description = "Read an image file and return the picture itself, so a multimodal model can look at it. "
                          + $"Supported formats: {ImageFormats.Supported}. An image larger than {FileLimits.ImageBytes} bytes is refused. "
                          + "The structured result also carries the media type, byte size and pixel dimensions. "
                          + "For a file that is not an image use read_text_file, or get_file_info for metadata only. "
                          + "The path must be absolute and covered by a directory grant with 'read' access."
        });

    [McpServerTool(Name = "read_image_file", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ImageFileResult))]
    private async Task<CallToolResult> ReadAsync(
        [Description("Absolute path of the image file to read.")] [MaxLength(4096)] string path,
        CancellationToken cancellationToken = default)
    {
        string resolved;
        try
        {
            resolved = guard.Resolve(path, GrantCapability.Read);
        }
        catch (GrantException error)
        {
            return reply.Reply(new ImageFileResult(path, "", 0, null, null, false, error.Message), true);
        }

        if (await files.DirectoryExistsAsync(resolved, cancellationToken))
        {
            return reply.Reply(new ImageFileResult(resolved, "", 0, null, null, false, "Path is a directory. Use list_directory."), true);
        }

        byte[] data;
        try
        {
            var entry = await files.GetEntryAsync(resolved, cancellationToken);
            if (entry is null)
            {
                return reply.Reply(new ImageFileResult(resolved, "", 0, null, null, false, "File does not exist."), true);
            }

            var length = entry.Length;
            if (length == 0)
            {
                return reply.Reply(new ImageFileResult(resolved, "", 0, null, null, false, "File is empty."), true);
            }

            if (length > FileLimits.ImageBytes)
            {
                return reply.Reply(new ImageFileResult(resolved, "", length, null, null, false,
                    $"Image is {length} bytes, which exceeds the {FileLimits.ImageBytes} byte limit."), true);
            }

            var bytes = await files.ReadBytesAsync(resolved, cancellationToken);
            if (bytes is null)
            {
                return reply.Reply(new ImageFileResult(resolved, "", 0, null, null, false, "File does not exist."), true);
            }

            data = bytes;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return reply.Reply(new ImageFileResult(resolved, "", 0, null, null, false, error.Message), true);
        }

        if (ImageFormats.Detect(data) is not { } format)
        {
            return reply.Reply(new ImageFileResult(resolved, "", data.Length, null, null, false,
                $"Not a supported image format ({ImageFormats.Supported}). Use read_text_file for text, or get_file_info for metadata."), true);
        }

        var size = ImageFormats.Measure(data, format);
        var result = new ImageFileResult(resolved, format, data.Length, size?.Width, size?.Height, true, null);
        return reply.ReplyImage(result, data, format);
    }

    /// <summary>
    /// Recognizes the image formats the Host can store and the chat client can send, by the bytes
    /// themselves rather than by the file name: an extension is a claim, and the model is about to
    /// receive the data declared by the media type this returns.
    /// </summary>
    private static class ImageFormats
    {
        public const string Supported = "PNG, JPEG, WebP, GIF";

        public static string? Detect(byte[] data)
        {
            if (data.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
            if (data.AsSpan().StartsWith(new byte[] { 255, 216, 255 })) return "image/jpeg";
            if (data.Length >= 12 && data.AsSpan().StartsWith("RIFF"u8) && data.AsSpan(8).StartsWith("WEBP"u8)) return "image/webp";
            if (data.AsSpan().StartsWith("GIF87a"u8) || data.AsSpan().StartsWith("GIF89a"u8)) return "image/gif";
            return null;
        }

        /// <summary>
        /// Pixel dimensions from the file header, or <c>null</c> when it does not parse. This is a
        /// reported fact, not a requirement: an unparsable header must not stop the picture itself
        /// from reaching the model.
        /// </summary>
        public static (int Width, int Height)? Measure(byte[] data, string mediaType) => mediaType switch
        {
            "image/png" => Png(data),
            "image/jpeg" => Jpeg(data),
            "image/webp" => Webp(data),
            "image/gif" => Gif(data),
            _ => null,
        };

        private static (int Width, int Height)? Png(byte[] data) =>
            data.Length >= 24 && data.AsSpan(12).StartsWith("IHDR"u8)
                ? (ReadUInt32BigEndian(data, 16), ReadUInt32BigEndian(data, 20))
                : null;

        private static (int Width, int Height)? Gif(byte[] data) =>
            data.Length >= 10 ? (ReadUInt16LittleEndian(data, 6), ReadUInt16LittleEndian(data, 8)) : null;

        /// <summary>
        /// Walks the JPEG segment chain to the frame header. The scan is bounded by the
        /// <c>Start of Scan</c> marker, past which no frame header follows.
        /// </summary>
        private static (int Width, int Height)? Jpeg(byte[] data)
        {
            var index = 2;
            while (index + 1 < data.Length)
            {
                if (data[index] != 0xFF)
                {
                    index++;
                    continue;
                }

                var marker = data[index + 1];
                index += 2;
                // Padding bytes between segments, and the markers that carry no payload.
                if (marker == 0xFF || marker == 0x01 || marker is >= 0xD0 and <= 0xD8) continue;
                if (index + 2 > data.Length) return null;
                var length = ReadUInt16BigEndian(data, index);
                if (length < 2) return null;
                // C4 (Huffman tables), C8 and CC share the C0..CF range but are not frame headers.
                if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
                {
                    return index + 7 <= data.Length
                        ? (ReadUInt16BigEndian(data, index + 5), ReadUInt16BigEndian(data, index + 3))
                        : null;
                }

                if (marker == 0xDA) return null;
                index += length;
            }

            return null;
        }

        private static (int Width, int Height)? Webp(byte[] data)
        {
            if (data.Length < 30) return null;
            return System.Text.Encoding.ASCII.GetString(data, 12, 4) switch
            {
                // Extended format: a 24-bit canvas size follows the flags and reserved bytes.
                "VP8X" => (ReadUInt24LittleEndian(data, 24) + 1, ReadUInt24LittleEndian(data, 27) + 1),
                // Lossy: after the 3-byte frame tag and the 3-byte start code come 14-bit values.
                "VP8 " => (ReadUInt16LittleEndian(data, 26) & 0x3FFF, ReadUInt16LittleEndian(data, 28) & 0x3FFF),
                // Lossless: 14-bit width then height, each biased by one, packed little-endian.
                "VP8L" when data.Length >= 25 => ((ReadUInt32LittleEndian(data, 21) & 0x3FFF) + 1,
                    ((ReadUInt32LittleEndian(data, 21) >> 14) & 0x3FFF) + 1),
                _ => null,
            };
        }

        // Byte order is a property of each format, never a default: PNG and JPEG store their
        // integers big-endian, GIF and both WebP variants little-endian. A reader that does not
        // say which one it is invites exactly the mistake this replaced — a JPEG length read
        // backwards, which silently skips past the frame header and reports no dimensions.
        private static int ReadUInt16LittleEndian(byte[] data, int offset) =>
            data[offset] | (data[offset + 1] << 8);

        private static int ReadUInt16BigEndian(byte[] data, int offset) =>
            (data[offset] << 8) | data[offset + 1];

        private static int ReadUInt32BigEndian(byte[] data, int offset) =>
            (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

        private static int ReadUInt32LittleEndian(byte[] data, int offset) =>
            data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);

        private static int ReadUInt24LittleEndian(byte[] data, int offset) =>
            data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
    }
}

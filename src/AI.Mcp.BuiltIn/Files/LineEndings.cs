namespace AI.Mcp.BuiltIn.Files;

using AI.Contracts.FileSystem;

/// <summary>
/// Reads hand the model LF text and say when the file uses CRLF; writes put CRLF back. In a JSON
/// result every CRLF is four escaped characters and usually an extra token, once per line of every
/// file read, while to the model the two styles mean the same text.
/// </summary>
internal static class LineEndings
{
    public const string Crlf = "CRLF";

    /// <summary>
    /// The text with CRLF breaks as LF, and <see cref="Crlf"/>, when every break in it is CRLF;
    /// otherwise the text unchanged and null, so a file with mixed breaks is never rewritten on a
    /// round trip.
    /// </summary>
    public static (string Text, string? Style) ForModel(string text)
    {
        var crlf = 0;
        var lf = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n') continue;
            lf++;
            if (index > 0 && text[index - 1] == '\r') crlf++;
        }

        return crlf > 0 && crlf == lf ? (text.Replace("\r\n", "\n", StringComparison.Ordinal), Crlf) : (text, null);
    }

    /// <summary>Whether the first line break of an existing file is CRLF.</summary>
    public static async Task<bool> IsCrlfAsync(IFileSystem files, string path, CancellationToken cancellationToken)
    {
        await using var stream = await files.OpenReadAsync(path, cancellationToken);
        if (stream is null) return false;
        var buffer = new byte[FileLimits.BinaryProbeBytes];
        var read = await stream.ReadAsync(buffer, cancellationToken);
        var index = Array.IndexOf(buffer, (byte)'\n', 0, read);
        return index > 0 && buffer[index - 1] == (byte)'\r';
    }
}

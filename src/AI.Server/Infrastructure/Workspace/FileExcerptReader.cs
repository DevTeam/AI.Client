namespace AI.Infrastructure.Workspace;

using System.Globalization;
using System.Text;
using AI.Application.Resources;
using AI.Contracts.FileSystem;

/// <summary>
/// Reads the lines a message points at. The text is kept with the message, so it is bounded: a
/// range longer than the limits is cut and says so, and the model reads the rest itself.
/// </summary>
public sealed class FileExcerptReader(IFileSystem files, IPath paths) : IFileExcerptReader
{
    public const int LineLimit = 1_000;
    public const int CharacterLimit = 48 * 1024;

    public async Task<string> ReadLinesAsync(string path, int first, int last, CancellationToken cancellationToken)
    {
        if (first < 1 || last < first) throw new ArgumentException("A line range starts at 1 and does not end before it starts.");
        await using var stream = await files.OpenReadAsync(path, cancellationToken)
            ?? throw new FileNotFoundException($"'{path}' was not found.", path);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = new StringBuilder();
        var number = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (++number < first) continue;
            if (number > last) break;
            if (number - first >= LineLimit || text.Length + line.Length > CharacterLimit)
            {
                text.Append(CultureInfo.InvariantCulture, $"[cut at line {number - 1}: read the rest of the file with a tool]");
                return text.ToString();
            }
            text.Append(line).Append('\n');
        }
        if (number < first) throw new ArgumentException($"The file has {number} lines; line {first} is past its end.");
        return text.ToString();
    }

    public async Task<byte[]> ReadAllAsync(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var stream = await files.OpenReadAsync(path, cancellationToken)
            ?? throw new FileNotFoundException($"'{path}' was not found.", path);
        if (stream.Length > maximumBytes)
            throw new InvalidDataException($"{paths.GetFileName(path)} is larger than {maximumBytes / (1024 * 1024)} MB.");
        // Read to the end rather than to the length: a file still being written may have grown.
        using var buffer = new MemoryStream((int)stream.Length);
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maximumBytes)
                throw new InvalidDataException($"{paths.GetFileName(path)} is larger than {maximumBytes / (1024 * 1024)} MB.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}

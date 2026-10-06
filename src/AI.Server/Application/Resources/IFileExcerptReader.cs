namespace AI.Application.Resources;

/// <summary>Reads a span of lines of a text file for a message that points at them.</summary>
public interface IFileExcerptReader
{
    /// <summary>
    /// Lines <paramref name="first"/>..<paramref name="last"/> (1-based, inclusive) of the file,
    /// clipped to its end. Throws <see cref="ArgumentException"/> when the range starts past it.
    /// </summary>
    Task<string> ReadLinesAsync(string path, int first, int last, CancellationToken cancellationToken);

    /// <summary>
    /// The whole file, for a message that sends it with its content. Throws
    /// <see cref="InvalidDataException"/> when it is larger than <paramref name="maximumBytes"/>.
    /// </summary>
    Task<byte[]> ReadAllAsync(string path, int maximumBytes, CancellationToken cancellationToken);
}

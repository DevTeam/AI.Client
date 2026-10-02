namespace AI.TextCorrection.Resources;

/// <summary>Versioned, deterministic index of three UTF-16 letters. Shared with the build tool.</summary>
public interface ITrigramIndexFormat
{
    ulong Encode(char first, char second, char third);
    void AddWord(ReadOnlySpan<char> word, ISet<ulong> index);
    void Write(Stream stream, IEnumerable<ulong> index, ReadOnlySpan<byte> sourceHash);
    ulong[] Read(Stream stream);
}

public sealed class TrigramIndexFormat : ITrigramIndexFormat
{
    private readonly uint _magic = 0x31474354; // TCG1, little endian; changing the format changes this version.
    private readonly int _hashLength = 32;

    public ulong Encode(char first, char second, char third) =>
        ((ulong)char.ToLowerInvariant(first) << 32) | ((ulong)char.ToLowerInvariant(second) << 16) | char.ToLowerInvariant(third);

    public void AddWord(ReadOnlySpan<char> word, ISet<ulong> index)
    {
        var end = word.IndexOfAny('/', '\t', ' ');
        if (end >= 0) word = word[..end];
        foreach (var character in word)
            if (!char.IsLetter(character)) return;
        for (var offset = 0; offset + 2 < word.Length; offset++)
            index.Add(Encode(word[offset], word[offset + 1], word[offset + 2]));
    }

    public void Write(Stream stream, IEnumerable<ulong> index, ReadOnlySpan<byte> sourceHash)
    {
        if (sourceHash.Length != _hashLength) throw new ArgumentException("Expected a SHA-256 source hash.", nameof(sourceHash));
        var values = index.Distinct().Order().ToArray();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(_magic);
        writer.Write(sourceHash);
        writer.Write(values.Length);
        foreach (var value in values) writer.Write(value);
    }

    public ulong[] Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != _magic) throw new InvalidDataException("Unsupported trigram index version.");
        if (reader.ReadBytes(_hashLength).Length != _hashLength) throw new InvalidDataException("Truncated source hash.");
        var count = reader.ReadInt32();
        if (count is < 0 or > 1_000_000) throw new InvalidDataException("Invalid trigram index size.");
        var values = new ulong[count];
        for (var offset = 0; offset < count; offset++)
        {
            values[offset] = reader.ReadUInt64();
            if (values[offset] > 0xFFFFFFFFFFFF || offset > 0 && values[offset] <= values[offset - 1])
                throw new InvalidDataException("Invalid trigram index ordering.");
        }
        if (stream.ReadByte() != -1) throw new InvalidDataException("Unexpected data after trigram index.");
        return values;
    }
}

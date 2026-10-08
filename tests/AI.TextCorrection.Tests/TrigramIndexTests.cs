namespace AI.TextCorrection.Tests;

using System.Security.Cryptography;
using System.Text;
using AI.TextCorrection.Resources;
using Shouldly;
using Xunit;

public sealed class TrigramIndexTests
{
    private readonly TrigramIndexFormat _format = new();

    [Fact]
    public void WritesDeterministicSortedDistinctIndexes()
    {
        var hash = SHA256.HashData("source"u8);
        using var first = new MemoryStream();
        using var second = new MemoryStream();
        _format.Write(first, [3, 1, 3, 2], hash);
        _format.Write(second, [2, 3, 1], hash);
        first.ToArray().ShouldBe(second.ToArray());
        first.Position = 0;
        _format.Read(first).ShouldBe([1UL, 2UL, 3UL]);
        first.CanRead.ShouldBeTrue();
    }

    [Theory]
    [InlineData("ÉCOLE/A", "éco")]
    [InlineData("РУССКИЙ\tmorphology", "рус")]
    [InlineData("mañana metadata", "mañ")]
    public void ExtractsUnicodeLettersAndIgnoresAffixFlags(string entry, string expected)
    {
        var values = new HashSet<ulong>();
        _format.AddWord(entry.AsSpan(), values);
        values.ShouldContain(_format.Encode(expected[0], expected[1], expected[2]));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("foo-bar/A")]
    [InlineData("a")]
    [InlineData("word123")]
    public void ExcludesNonLetterEntries(string entry)
    {
        var values = new HashSet<ulong>();
        _format.AddWord(entry.AsSpan(), values);
        values.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)] // magic
    [InlineData(36)] // count
    [InlineData(48)] // second value, making it equal to the first
    public void RejectsInvalidResources(int offset)
    {
        using var buffer = new MemoryStream();
        _format.Write(buffer, [1, 2], new byte[32]);
        var bytes = buffer.ToArray();
        bytes[offset] = offset == 36 ? (byte)255 : offset == 48 ? (byte)1 : (byte)0;
        if (offset == 36) bytes.AsSpan(36, 4).Fill(255);
        using var corrupt = new MemoryStream(bytes);
        Should.Throw<InvalidDataException>(() => _format.Read(corrupt));
    }

    [Fact]
    public void RejectsTruncatedAndTrailingData()
    {
        using var buffer = new MemoryStream();
        _format.Write(buffer, [1, 2], new byte[32]);
        using var truncated = new MemoryStream(buffer.ToArray()[..^1]);
        Should.Throw<EndOfStreamException>(() => _format.Read(truncated));
        using var extra = new MemoryStream([.. buffer.ToArray(), 0]);
        Should.Throw<InvalidDataException>(() => _format.Read(extra));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("fr")]
    [InlineData("es")]
    [Trait("Category", "Slow")]
    public void BundledIndexMatchesTheOriginalDictionary(string language)
    {
        var source = new EmbeddedTextDictionaries().All.Single(dictionary => dictionary.LanguageId == language);
        using var words = source.OpenWords();
        using var reader = new StreamReader(words, new UTF8Encoding(false, true));
        // Independent reference implementation of the original letter-triple semantics.
        var reference = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var end = line.IndexOfAny(['/', '\t', ' ']);
            var word = (end < 0 ? line : line[..end]).ToLowerInvariant();
            if (!word.All(char.IsLetter)) continue;
            for (var index = 0; index + 2 < word.Length; index++) reference.Add(word.Substring(index, 3));
        }
        using var resource = ((IPreparedTextDictionaryResource)source).OpenTrigrams();
        using var original = source.OpenWords();
        using (var header = new BinaryReader(resource, Encoding.UTF8, leaveOpen: true))
        {
            _ = header.ReadUInt32();
            header.ReadBytes(32).ShouldBe(SHA256.HashData(original),
                "Run Prepare Text Correction after importing dictionaries and commit the generated indexes.");
        }
        resource.Position = 0;
        var actual = _format.Read(resource);
        actual.ShouldBe(reference.Select(word => _format.Encode(word[0], word[1], word[2])).Order().ToArray());
    }

    [Fact]
    public async Task PreparedModelsNeverReadTheWordListAndReuseTheIndex()
    {
        var resource = new PreparedResource(_format);
        var model = new DictionaryWordPlausibility(resource, _format);
        await Task.WhenAll(model.PrepareAsync("test"), model.PrepareAsync("test"));
        model.Score("test", "HELLO").ShouldBe(1);
        model.Score("test", "hellx").ShouldBe(2d / 3);
        model.Score("test", "xy").ShouldBe(0);
        resource.Reads.ShouldBe(1);
    }

    private sealed class PreparedResource(ITrigramIndexFormat format) : IPreparedTextDictionaryResource, ITextDictionaries
    {
        public int Reads { get; private set; }
        public string LanguageId => "test";
        public IReadOnlyList<ITextDictionaryResource> All => [this];
        public Stream OpenWords() => throw new InvalidOperationException("Prepared model must not reopen the word list.");
        public Stream OpenAffixes() => throw new InvalidOperationException("Prepared model must not open affix rules.");
        public Stream OpenTrigrams()
        {
            Reads++;
            var buffer = new MemoryStream();
            var values = new HashSet<ulong>();
            format.AddWord("hello", values);
            format.Write(buffer, values, new byte[32]);
            buffer.Position = 0;
            return buffer;
        }
    }
}

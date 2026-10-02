namespace AI.TextCorrection;

using System.Reflection;

public sealed class EmbeddedTextDictionaries : ITextDictionaries
{
    public IReadOnlyList<ITextDictionaryResource> All { get; }

    public EmbeddedTextDictionaries()
    {
        var assembly = GetType().Assembly;
        var prefix = assembly.GetName().Name + ".Dictionaries.";
        All = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".index.dic", StringComparison.Ordinal))
            .Select(name => (ITextDictionaryResource)new EmbeddedTextDictionaryResource(assembly, name[prefix.Length..^10], name[..^4]))
            .ToArray();
    }
}

public sealed class EmbeddedTextDictionaryResource(Assembly assembly, string languageId, string resourcePrefix) : IPreparedTextDictionaryResource
{
    public string LanguageId { get; } = languageId;
    public Stream OpenWords() => Open(".dic");
    public Stream OpenAffixes() => Open(".aff");
    public Stream OpenTrigrams() => Open(".trigrams");
    private Stream Open(string suffix) => assembly.GetManifestResourceStream(resourcePrefix + suffix)
        ?? throw new InvalidOperationException($"Missing dictionary resource: {resourcePrefix}{suffix}.");
}

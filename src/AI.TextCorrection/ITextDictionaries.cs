namespace AI.TextCorrection;

public interface ITextDictionaryResource
{
    string LanguageId { get; }
    /// <summary>UTF-8 Hunspell word list, also used to learn letter patterns.</summary>
    Stream OpenWords();
    Stream OpenAffixes();
}

/// <summary>Optional prepared resource; custom providers can continue to supply word lists.</summary>
public interface IPreparedTextDictionaryResource : ITextDictionaryResource
{
    Stream OpenTrigrams();
}

public interface ITextDictionaries
{
    IReadOnlyList<ITextDictionaryResource> All { get; }
}

public interface IWordPlausibility
{
    double Score(string layoutId, string word);
}

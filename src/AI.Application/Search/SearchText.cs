namespace AI.Application.Search;

using System.Text.RegularExpressions;

/// <summary>
/// The word matching tool and skill search share. Queries come from the model in English, so a
/// word is compared by its plain forms only: "tests" finds "test" and "files" finds "file", and a
/// whole word never matches inside another one, as "read" would inside "thread".
/// </summary>
public static partial class SearchText
{
    /// <summary>Words that say nothing about a capability and would match nearly every entry.</summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "any", "are", "as", "at", "be", "by", "can", "do", "for", "from", "how", "i", "in",
        "into", "is", "it", "me", "my", "of", "on", "or", "please", "some", "that", "the", "this", "to",
        "tool", "tools", "use", "using", "want", "with", "you"
    };

    public const string EnglishOnly =
        "Search with English words, for example \"read text file\" or \"create sequence diagram\": names and descriptions are in English.";

    /// <summary>The query's capability words, each with its plain forms, stop words left out.</summary>
    public static IReadOnlyList<IReadOnlySet<string>> Terms(string query) =>
        Words(query).Where(word => !StopWords.Contains(word)).Distinct(StringComparer.Ordinal).Select(Forms).ToArray();

    /// <summary>Every form of every word in the texts, for matching terms against.</summary>
    public static IReadOnlySet<string> Index(IEnumerable<string?> texts) =>
        texts.SelectMany(text => Words(text ?? string.Empty)).SelectMany(Forms).ToHashSet(StringComparer.Ordinal);

    public static bool Matches(IReadOnlySet<string> term, IReadOnlySet<string> index) => term.Any(index.Contains);

    /// <summary>
    /// Whether a form of the term begins a word of the name, so "git" still finds "github_issue_get".
    /// Ranked below a whole word; a term inside a word, as "read" in "thread", is no match at all.
    /// </summary>
    public static bool BeginsAWordOf(IReadOnlySet<string> term, string name) =>
        Words(name).Any(word => term.Any(form => form.Length >= 3 && word.StartsWith(form, StringComparison.Ordinal)));

    /// <summary>
    /// False when the query has letters outside the Latin script. Such a query cannot match English
    /// metadata, and an error that says so makes the model translate it instead of concluding that
    /// nothing exists.
    /// </summary>
    public static bool IsEnglish(string query) =>
        !query.Any(character => char.IsLetter(character) && character > LatinEnd);

    /// <summary>The last code point of the Latin Extended-B block.</summary>
    private const int LatinEnd = 0x024F;

    /// <summary>Lowercase words split at any non-letter and at camelCase boundaries.</summary>
    public static IEnumerable<string> Words(string text) =>
        WordPattern().Matches(CamelBoundary().Replace(text, "$1 $2").ToLowerInvariant()).Select(match => match.Value);

    private static IReadOnlySet<string> Forms(string word)
    {
        var forms = new HashSet<string>(StringComparer.Ordinal) { word };
        if (word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal)) forms.Add(word[..^3] + "y");
        if (word.Length > 3 && word.EndsWith("es", StringComparison.Ordinal)) forms.Add(word[..^2]);
        if (word.Length > 2 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal)) forms.Add(word[..^1]);
        return forms;
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"(\p{Ll}|\p{N})(\p{Lu})", RegexOptions.CultureInvariant)]
    private static partial Regex CamelBoundary();
}

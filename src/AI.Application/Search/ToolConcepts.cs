namespace AI.Application.Search;

using System.Collections;
using System.Globalization;
using System.Resources;
using Contracts.Tools;
using Tools;

/// <summary>Concept keywords from the resources, loaded once on first use, and each tool's concepts.</summary>
public sealed class ToolConcepts : IToolConcepts
{
    /// <summary>The languages with keywords besides the neutral English ones.</summary>
    public static readonly string[] Languages =
        ["ar", "bn", "de", "es", "fr", "he", "hi", "id", "it", "ja", "ko", "pt", "ru", "th", "vi", "zh"];

    private static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);

    private static readonly Dictionary<string, IReadOnlySet<string>> BuiltIn = Map(
        ("list_directory", "File Directory List"),
        ("directory_tree", "File Directory List"),
        ("list_allowed_directories", "Directory List Access"),
        ("read_text_file", "File Read"),
        ("read_multiple_files", "File Read"),
        ("read_image_file", "File Read Image"),
        ("get_file_info", "File Directory"),
        ("write_file", "File Write Create"),
        ("edit_file", "File Edit"),
        ("create_directory", "Directory Create"),
        ("delete_file", "File Delete"),
        ("delete_directory", "Directory Delete"),
        ("move_file", "File Directory Move"),
        ("search_files", "File Directory Search"),
        ("grep_files", "File Search"),
        ("zip_list", "Archive List"),
        ("zip_read", "Archive Read"),
        ("zip_extract", "Archive"),
        ("zip_create", "Archive Create"),
        ("fetch", "Web"),
        ("process_run", "Run"),
        ("cs_run", "Run"));

    private static readonly Dictionary<string, IReadOnlySet<string>> App = Map(
        ("app_chats", "Chat"),
        ("app_projects", "Project"),
        ("app_memory", "Memory"),
        ("app_schedule", "Schedule"),
        ("app_theme", "Theme"),
        ("app_skills", "Skill"),
        ("skill_search", "Skill Search"),
        ("run_skill", "Skill Run"),
        ("app_instructions", "Instructions"),
        ("app_security", "Access"),
        ("app_navigate", "Navigate"));

    private readonly Lazy<Keyword[]> _keywords = new(Load);

    public IReadOnlySet<string> Find(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return None;
        var lower = text.ToLowerInvariant();
        return _keywords.Value.Where(keyword => Occurs(lower, keyword)).Select(keyword => keyword.Concept)
            .ToHashSet(StringComparer.Ordinal);
    }

    public IReadOnlySet<string> ServedBy(AgentTool tool)
    {
        var name = tool.ModelDefinition.Name;
        var table = name.StartsWith(ToolRef.BuiltInPrefix, StringComparison.Ordinal) ? BuiltIn
            : name.StartsWith(ToolRef.AppPrefix, StringComparison.Ordinal) ? App : null;
        return table?.GetValueOrDefault(tool.OriginalName) ?? None;
    }

    /// <summary>
    /// A keyword is a word stem, so it must begin a word: "файл" finds "файлы" but "read" is not
    /// found in "thread". Scripts that write words together or attach particles to them (Chinese,
    /// Japanese, Thai, Arabic, Hebrew) have no such boundary to rely on and match anywhere.
    /// </summary>
    private static bool Occurs(string text, Keyword keyword)
    {
        for (var index = text.IndexOf(keyword.Text, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(keyword.Text, index + 1, StringComparison.Ordinal))
            if (keyword.Anywhere || index == 0 || !IsWordCharacter(text[index - 1])) return true;
        return false;
    }

    private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character)
        || CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;

    private static bool Unbounded(char character) => character is >= '֐' and <= 'ۿ' // Hebrew, Arabic
        or >= '฀' and <= '๿' // Thai
        or >= '぀' and <= 'ヿ' // Hiragana, Katakana
        or >= '㐀' and <= '鿿'; // CJK ideographs

    private static Keyword[] Load()
    {
        var resources = new ResourceManager(typeof(ToolConcepts).FullName!, typeof(ToolConcepts).Assembly);
        return new[] { CultureInfo.InvariantCulture }.Concat(Languages.Select(CultureInfo.GetCultureInfo))
            // Only the culture's own entries: a fallback to English would repeat the neutral keywords.
            .Select(culture => resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false))
            .Where(set => set is not null)
            .SelectMany(set => set!.Cast<DictionaryEntry>())
            .SelectMany(entry => ((string)entry.Value!).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(text => text.ToLowerInvariant())
                .Select(text => new Keyword(text, (string)entry.Key, text.Any(Unbounded))))
            .Distinct()
            .ToArray();
    }

    private static Dictionary<string, IReadOnlySet<string>> Map(params (string Tool, string Concepts)[] entries) =>
        entries.ToDictionary(entry => entry.Tool,
            entry => (IReadOnlySet<string>)entry.Concepts.Split(' ').ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

    private sealed record Keyword(string Text, string Concept, bool Anywhere);
}

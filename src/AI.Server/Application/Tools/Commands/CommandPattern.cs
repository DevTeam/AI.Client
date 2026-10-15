namespace AI.Application.Tools.Commands;

using System.Text;

/// <summary>
/// A command rule's pattern, such as <c>git diff *</c>: the program's name, then words matched one
/// by one against the command's subcommand and arguments.
/// </summary>
/// <remarks>
/// <para>
/// A word matches one argument: exactly, or as a mask when it holds <c>*</c> or <c>?</c>, which then
/// stand for characters inside that one argument (<c>--force*</c>). A lone <c>*</c> as the last word
/// matches whatever follows, nothing included. Without it the command has to end where the pattern
/// does. Words are separated by spaces; one that holds a space is written in double quotes, and a
/// quote inside quotes is doubled. Backslashes mean nothing, so Windows paths are written as they are.
/// </para>
/// <para>
/// No regular expressions: what a pattern allows can be read off it, and a call can be checked
/// against it without a matcher that could be made to take forever.
/// </para>
/// </remarks>
public sealed class CommandPattern
{
    public const int MaxLength = 2_000;

    private CommandPattern(string text, string name, IReadOnlyList<string> words, bool openEnded)
    {
        Text = text;
        Name = name;
        Words = words;
        OpenEnded = openEnded;
    }

    /// <summary>The pattern as stored, spelled the canonical way.</summary>
    public string Text { get; }

    /// <summary>The program, named as <see cref="ParsedCommand.Name"/> names it.</summary>
    public string Name { get; }

    /// <summary>The words after the program, without a trailing lone <c>*</c>.</summary>
    public IReadOnlyList<string> Words { get; }

    /// <summary>Whether a trailing lone <c>*</c> lets any further arguments follow.</summary>
    public bool OpenEnded { get; }

    /// <summary>Whether the pattern matches only the one command it spells.</summary>
    public bool IsExact => !OpenEnded && Words.All(word => !IsMask(word));

    /// <summary>
    /// Orders patterns from least to most specific: more words matched exactly, then a pattern that
    /// ends where the command must, then more literal characters.
    /// </summary>
    public (int Literal, int Closed, int Characters) Specificity =>
        (Words.Count(word => !IsMask(word)), OpenEnded ? 0 : 1, Words.Sum(word => word.Count(character => character is not ('*' or '?'))));

    public static bool TryParse(string? text, bool caseSensitive, out CommandPattern? pattern, out string? error)
    {
        pattern = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "A command pattern cannot be empty.";
            return false;
        }
        if (text.Length > MaxLength)
        {
            error = $"A command pattern is limited to {MaxLength} characters.";
            return false;
        }
        if (!TrySplit(text, out var words))
        {
            error = "A command pattern has an unclosed quote.";
            return false;
        }
        if (words.Count == 0 || IsMask(words[0]))
        {
            error = "A command pattern starts with the program's name, without * or ?.";
            return false;
        }
        var name = CommandNames.Normalize(words[0], caseSensitive);
        var openEnded = words.Count > 1 && words[^1] == "*";
        var rest = words.Skip(1).Take(words.Count - (openEnded ? 2 : 1)).ToArray();
        pattern = new CommandPattern(Format(openEnded ? [name, .. rest, "*"] : [name, .. rest]), name, rest, openEnded);
        error = null;
        return true;
    }

    public static CommandPattern Parse(string text, bool caseSensitive) =>
        TryParse(text, caseSensitive, out var pattern, out var error) ? pattern! : throw new ArgumentException(error, nameof(text));

    /// <summary>Whether the command is this program doing what the words say.</summary>
    public bool Matches(ParsedCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!string.Equals(Name, command.Name, StringComparison.Ordinal)) return false;
        var tokens = command.Tokens;
        if (tokens.Count < Words.Count || !OpenEnded && tokens.Count != Words.Count) return false;
        for (var index = 0; index < Words.Count; index++)
            if (!WordMatches(Words[index], tokens[index])) return false;
        return true;
    }

    /// <summary>Writes words so that <see cref="TryParse"/> reads them back unchanged.</summary>
    public static string Format(IEnumerable<string> words) => string.Join(' ', words.Select(Quote));

    /// <summary>Whether the word stands for more than itself.</summary>
    public static bool IsMask(string word) => word.AsSpan().IndexOfAny('*', '?') >= 0;

    private static string Quote(string word) =>
        word.Length > 0 && !word.Any(character => char.IsWhiteSpace(character) || character == '"')
            ? word
            : "\"" + word.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static bool TrySplit(string text, out List<string> words)
    {
        words = [];
        var current = new StringBuilder();
        var quoted = false;
        var started = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character != '"') current.Append(character);
                else if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else quoted = false;
            }
            else if (character == '"')
            {
                quoted = true;
                started = true;
            }
            else if (char.IsWhiteSpace(character))
            {
                if (started) words.Add(current.ToString());
                current.Clear();
                started = false;
            }
            else
            {
                current.Append(character);
                started = true;
            }
        }
        if (quoted) return false;
        if (started) words.Add(current.ToString());
        return true;
    }

    private static bool WordMatches(string word, string token) =>
        IsMask(word) ? Masks.Matches(word, token, ignoreCase: false) : string.Equals(word, token, StringComparison.Ordinal);
}

/// <summary>How rules and commands name a program.</summary>
public static class CommandNames
{
    /// <summary>
    /// The file name without <c>.exe</c> or <c>.com</c>, in lower case where names ignore case.
    /// Scripts keep their extension: <c>build.cmd</c> is not a program called build.
    /// </summary>
    public static string Normalize(string executable, bool caseSensitive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        var separator = executable.LastIndexOfAny(['/', '\\']);
        var name = separator >= 0 ? executable[(separator + 1)..] : executable;
        if (!caseSensitive)
        {
            name = name.ToLowerInvariant();
            if (name.EndsWith(".exe", StringComparison.Ordinal) || name.EndsWith(".com", StringComparison.Ordinal))
                name = name[..^4];
        }
        return name;
    }
}

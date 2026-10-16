namespace AI.Mcp.BuiltIn.Files;

using System.Text;
using System.Text.RegularExpressions;
using AI.Contracts.FileSystem;

/// <summary>
/// The ignore rules of a Git repository as a walk meets them: the repository's <c>info/exclude</c>,
/// the <c>.gitignore</c> of every directory from the repository root down to where the walk
/// starts, and each directory's own as the walk enters it. A walk outside any repository has no
/// rules, as Git itself would apply none.
/// </summary>
/// <remarks>
/// Searches and trees skip what the repository ignores because that is what makes them cheap for
/// the model: a worktree copy, a log or a generated folder can hold more entries than the sources
/// themselves, and every one of them is a path the model has to read past. Rules are matched as
/// Git matches them — the last matching rule wins, <c>!</c> re-includes, a trailing <c>/</c>
/// applies to directories only, and a pattern with a slash is anchored to its file's directory —
/// except that the global excludes file is not read.
/// </remarks>
internal sealed class IgnoreRules
{
    private const string FileName = ".gitignore";

    public static readonly IgnoreRules None = new(null, string.Empty, [], false, StringComparison.Ordinal);

    private readonly IgnoreRules? _parent;
    private readonly string _directory;
    private readonly Rule[] _rules;
    private readonly bool _active;
    private readonly StringComparison _comparison;

    private IgnoreRules(IgnoreRules? parent, string directory, Rule[] rules, bool active, StringComparison comparison)
    {
        _parent = parent;
        _directory = directory;
        _rules = rules;
        _active = active;
        _comparison = comparison;
    }

    /// <summary>The rules in force at <paramref name="root"/>, its own <c>.gitignore</c> included.</summary>
    public static async Task<IgnoreRules> ForRootAsync(IFileSystem files, IPath paths, string root, CancellationToken cancellationToken)
    {
        var chain = new List<string>();
        string? repository = null;
        for (var current = root; current is not null; current = paths.GetDirectoryName(current))
        {
            chain.Add(current);
            var marker = paths.Combine(current, ".git");
            if (await files.DirectoryExistsAsync(marker, cancellationToken) || await files.FileExistsAsync(marker, cancellationToken))
            {
                repository = current;
                break;
            }
        }

        if (repository is null) return None;
        var comparison = paths.Comparison;
        var rules = new IgnoreRules(null, repository, [], true, comparison);
        if (await ExcludeFileAsync(files, paths, repository, cancellationToken) is { } exclude)
            rules = new IgnoreRules(rules, repository, await ReadAsync(files, exclude, comparison, cancellationToken), true, comparison);
        chain.Reverse();
        foreach (var directory in chain)
        {
            var ignore = paths.Combine(directory, FileName);
            if (await files.FileExistsAsync(ignore, cancellationToken))
                rules = new IgnoreRules(rules, directory, await ReadAsync(files, ignore, comparison, cancellationToken), true, comparison);
        }

        return rules;
    }

    /// <summary>
    /// The rules for the children of <paramref name="directory"/>, given its listing: its own
    /// <c>.gitignore</c> is read only when the listing has one, so a walk pays nothing extra for
    /// the directories without.
    /// </summary>
    public async Task<IgnoreRules> EnterAsync(IFileSystem files, IPath paths, string directory,
        IReadOnlyList<FileSystemEntry> children, CancellationToken cancellationToken)
    {
        if (!_active || !children.Any(child => !child.IsDirectory && child.Name == FileName)) return this;
        return new IgnoreRules(this, directory,
            await ReadAsync(files, paths.Combine(directory, FileName), _comparison, cancellationToken), true, _comparison);
    }

    public bool IsIgnored(IPath paths, string path, bool isDirectory) =>
        _active && Decide(paths, path, isDirectory) == true;

    private bool? Decide(IPath paths, string path, bool isDirectory)
    {
        var verdict = _parent?.Decide(paths, path, isDirectory);
        if (_rules.Length == 0) return verdict;
        var relative = paths.GetRelativePath(_directory, path).Replace('\\', '/');
        foreach (var rule in _rules)
            if ((!rule.DirectoryOnly || isDirectory) && rule.Pattern.IsMatch(relative))
                verdict = !rule.Negated;
        return verdict;
    }

    /// <summary>
    /// The repository's <c>info/exclude</c>. A linked worktree's <c>.git</c> is a file naming its
    /// own git directory, whose <c>commondir</c> names the shared one that holds <c>info</c>.
    /// </summary>
    private static async Task<string?> ExcludeFileAsync(IFileSystem files, IPath paths, string repository,
        CancellationToken cancellationToken)
    {
        var marker = paths.Combine(repository, ".git");
        var gitDirectory = marker;
        if (await files.FileExistsAsync(marker, cancellationToken))
        {
            var pointer = (await files.ReadTextAsync(marker, cancellationToken))?.Trim();
            if (pointer is null || !pointer.StartsWith("gitdir:", StringComparison.Ordinal)) return null;
            gitDirectory = paths.GetFullPath(pointer["gitdir:".Length..].Trim(), repository);
            var common = paths.Combine(gitDirectory, "commondir");
            if (await files.FileExistsAsync(common, cancellationToken)
                && (await files.ReadTextAsync(common, cancellationToken))?.Trim() is { Length: > 0 } shared)
                gitDirectory = paths.GetFullPath(shared, gitDirectory);
        }

        var exclude = paths.Combine(paths.Combine(gitDirectory, "info"), "exclude");
        return await files.FileExistsAsync(exclude, cancellationToken) ? exclude : null;
    }

    private static async Task<Rule[]> ReadAsync(IFileSystem files, string path, StringComparison comparison,
        CancellationToken cancellationToken)
    {
        var rules = new List<Rule>();
        try
        {
            await foreach (var line in files.ReadLinesAsync(path, cancellationToken))
                if (Rule.Parse(line, comparison) is { } rule) rules.Add(rule);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // An unreadable ignore file hides nothing; the walk still sees every entry.
        }

        return rules.ToArray();
    }

    private sealed record Rule(Regex Pattern, bool Negated, bool DirectoryOnly)
    {
        public static Rule? Parse(string line, StringComparison comparison)
        {
            var text = TrimTrailingSpaces(line);
            if (text.Length == 0 || text[0] == '#') return null;
            var negated = text[0] == '!';
            if (negated) text = text[1..];
            else if (text.StartsWith("\\!", StringComparison.Ordinal) || text.StartsWith("\\#", StringComparison.Ordinal))
                text = text[1..];
            var directoryOnly = text.EndsWith('/');
            if (directoryOnly) text = text.TrimEnd('/');
            if (text.Length == 0) return null;
            // A slash anywhere but at the end ties the pattern to its file's directory; without one
            // it matches a name at any depth below it.
            var anchored = text.Contains('/', StringComparison.Ordinal);
            text = text.TrimStart('/');
            var options = RegexOptions.CultureInvariant
                          | (comparison == StringComparison.OrdinalIgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            try
            {
                return new Rule(new Regex((anchored ? "^" : "^(?:.*/)?") + Translate(text) + "$", options,
                    TimeSpan.FromSeconds(1)), negated, directoryOnly);
            }
            catch (ArgumentException)
            {
                // Git skips a pattern it cannot read, and so does this.
                return null;
            }
        }

        private static string TrimTrailingSpaces(string line)
        {
            var end = line.Length;
            while (end > 0 && line[end - 1] is ' ' or '\t' or '\r' && !(end > 1 && line[end - 2] == '\\')) end--;
            return line[..end];
        }

        private static string Translate(string pattern)
        {
            var builder = new StringBuilder();
            for (var index = 0; index < pattern.Length; index++)
            {
                var character = pattern[index];
                if (character == '*' && index + 1 < pattern.Length && pattern[index + 1] == '*')
                {
                    var leading = index == 0 || pattern[index - 1] == '/';
                    var trailing = index + 2 == pattern.Length || pattern[index + 2] == '/';
                    index++;
                    if (leading && trailing && index + 1 < pattern.Length)
                    {
                        // "**/" matches zero or more whole directories.
                        builder.Append("(?:.*/)?");
                        index++;
                    }
                    else
                    {
                        builder.Append(".*");
                    }

                    continue;
                }

                switch (character)
                {
                    case '*':
                        builder.Append("[^/]*");
                        break;
                    case '?':
                        builder.Append("[^/]");
                        break;
                    case '[' when pattern.IndexOf(']', index + 1) is var close and > 0:
                        var set = pattern[(index + 1)..close];
                        builder.Append('[').Append(set.StartsWith('!') ? "^" + Regex.Escape(set[1..]) : Regex.Escape(set)).Append(']');
                        index = close;
                        break;
                    case '\\' when index + 1 < pattern.Length:
                        builder.Append(Regex.Escape(pattern[++index].ToString()));
                        break;
                    default:
                        builder.Append(Regex.Escape(character.ToString()));
                        break;
                }
            }

            return builder.ToString();
        }
    }
}

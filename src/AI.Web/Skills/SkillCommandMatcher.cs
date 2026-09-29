namespace AI.Web.Skills;

using AI.Contracts.Skills;

public sealed class SkillCommandMatcher : ISkillCommandMatcher
{
    public string? GetQuery(string text)
    {
        // Only a message that is nothing but "/word" so far: a slash later in the text, or a path
        // such as "/usr/bin", is ordinary text.
        if (text.Length == 0 || text[0] != '/') return null;
        var query = text[1..];
        return query.Any(character => char.IsWhiteSpace(character) || character == '/') ? null : query;
    }

    public IReadOnlyList<SkillCommandMatch> Match(IReadOnlyList<SkillDefinition> skills, string query,
        IReadOnlyList<string> recentIds)
    {
        var effective = skills
            .GroupBy(skill => skill.Id, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(skill => Priority(skill.Source)).First())
            .Where(skill => skill.Enabled);
        var matches = new List<(SkillCommandMatch Match, int Rank)>();
        foreach (var skill in effective)
        {
            if (Rank(skill, query) is { } ranked)
                matches.Add((new SkillCommandMatch(skill, ranked.Highlights, ranked.Alias), ranked.Rank));
        }
        return matches
            .OrderBy(item => item.Rank)
            .ThenBy(item => RecentIndex(recentIds, item.Match.Skill.Id))
            .ThenByDescending(item => Priority(item.Match.Skill.Source))
            .ThenBy(item => item.Match.Skill.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => item.Match)
            .ToArray();
    }

    private static (int Rank, IReadOnlyList<int> Highlights, string? Alias)? Rank(SkillDefinition skill, string query)
    {
        if (query.Length == 0) return (0, [], null);
        var aliases = skill.Aliases ?? [];
        const StringComparison ordinal = StringComparison.OrdinalIgnoreCase;
        // "/compact" typed in full goes straight to the skill that claims it.
        if (aliases.FirstOrDefault(alias => alias.Equals(query, ordinal)) is { } exact)
            return (-1, Range(0, query.Length), exact);
        var name = skill.Name;
        const StringComparison ignoreCase = StringComparison.CurrentCultureIgnoreCase;
        if (name.StartsWith(query, ignoreCase)) return (0, Range(0, query.Length), null);
        if (aliases.FirstOrDefault(alias => alias.StartsWith(query, ordinal)) is { } prefix)
            return (1, Range(0, query.Length), prefix);
        if (skill.Id.StartsWith(query, ordinal)) return (1, [], null);
        for (var index = name.IndexOf(query, ignoreCase); index > 0; index = name.IndexOf(query, index + 1, ignoreCase))
        {
            if (!char.IsLetterOrDigit(name[index - 1])) return (2, Range(index, query.Length), null);
        }
        if (name.IndexOf(query, ignoreCase) is var inside and >= 0) return (3, Range(inside, query.Length), null);
        if (skill.Id.Contains(query, ordinal)) return (3, [], null);
        // A mistyped command such as "/coma" still reads better as the alias it almost is.
        foreach (var alias in aliases)
            if (Subsequence(alias, query) is { } scatteredAlias) return (4, scatteredAlias, alias);
        if (Subsequence(name, query) is { } scattered) return (5, scattered, null);
        if (skill.Description.Contains(query, ignoreCase)) return (6, [], null);
        return null;
    }

    /// <summary>"cht" finds "Chat title": each typed character in order, anywhere in the text.</summary>
    private static List<int>? Subsequence(string name, string query)
    {
        var highlights = new List<int>(query.Length);
        var position = 0;
        foreach (var character in query)
        {
            var found = -1;
            for (var index = position; index < name.Length; index++)
            {
                if (char.ToUpperInvariant(name[index]) != char.ToUpperInvariant(character)) continue;
                found = index;
                break;
            }
            if (found < 0) return null;
            highlights.Add(found);
            position = found + 1;
        }
        return highlights;
    }

    private static int[] Range(int start, int length) => Enumerable.Range(start, length).ToArray();

    private static int RecentIndex(IReadOnlyList<string> recentIds, string id)
    {
        for (var index = 0; index < recentIds.Count; index++)
            if (recentIds[index] == id) return index;
        return int.MaxValue;
    }

    private static int Priority(string source) => source switch { "Project" => 2, "User" => 1, _ => 0 };
}

namespace AI.Application.Skills;

using System.Text.RegularExpressions;
using AI.Contracts.Skills;

/// <summary>Ranks skill metadata without sending the complete catalog to a model.</summary>
public sealed partial class SkillCandidateSelector : ISkillCandidateSelector
{
    private static readonly (string Prefix, string English)[] Translations =
    [
        ("созд", "create"), ("проект", "project"), ("прилож", "app"),
        ("навык", "skill"), ("скилл", "skill"), ("тест", "test"),
        ("ошиб", "bug"), ("исправ", "fix"), ("код", "code"),
        ("чат", "chat"), ("памят", "memory"), ("настро", "configure"),
        ("поиск", "search"), ("найд", "search"), ("обнов", "update"),
        ("документ", "docs"), ("коммит", "commit"), ("ветк", "branch"),
        ("диаграм", "diagram"), ("рисун", "svg"), ("сборк", "build"),
        ("проверк", "review"), ("ревью", "review"), ("импорт", "import"),
        ("сервер", "server"), ("модел", "model"), ("подключ", "connection"),
        ("решил", "summary"), ("договор", "summary"), ("decid", "summary"),
        ("agre", "summary"), ("recap", "summary")
    ];

    public IReadOnlyList<SkillDefinition> FindCandidates(string query, IReadOnlyList<SkillDefinition> skills, int limit,
        string? activeSkillId = null)
    {
        var words = Words(query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var word in words.ToArray())
            foreach (var (prefix, english) in Translations)
                if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) words.Add(english);

        var ranked = skills.Select(skill => new { Skill = skill, Score = Score(skill, query, words) })
            .Where(item => item.Score >= (words.Count > 1 ? 4 : 2) || item.Skill.Id == activeSkillId)
            .OrderByDescending(item => item.Skill.Id == activeSkillId)
            .ThenByDescending(item => item.Score)
            .ThenBy(item => item.Skill.Id, StringComparer.Ordinal)
            .Take(Math.Max(0, limit))
            .Select(item => item.Skill)
            .ToArray();
        return ranked;
    }

    private static int Score(SkillDefinition skill, string query, IReadOnlySet<string> words)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0;
        if (skill.Id.Equals(query.Trim().TrimStart('/'), StringComparison.OrdinalIgnoreCase)) return 1_000;
        if (skill.Aliases?.Any(alias => alias.Equals(query.Trim().TrimStart('/'), StringComparison.OrdinalIgnoreCase)) == true)
            return 900;
        var ids = Words(skill.Id).Concat(Words(skill.Name)).Concat(skill.Aliases?.SelectMany(Words) ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var description = Words(skill.Description).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return words.Sum(word => ids.Contains(word) ? 8 : description.Contains(word) ? 2 : 0);
    }

    private static IEnumerable<string> Words(string text) => WordPattern().Matches(text.ToLowerInvariant())
        .Select(match => match.Value).Where(word => word.Length > 2);

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
}

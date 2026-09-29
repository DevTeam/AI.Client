namespace AI.Web.Skills;

using AI.Contracts.Skills;

/// <summary>Picks and orders the skills the composer offers after a leading slash.</summary>
public interface ISkillCommandMatcher
{
    /// <summary>
    /// The query of a composer whose whole text is a slash command being typed ("/", "/ch"), or
    /// null when the text is an ordinary message.
    /// </summary>
    string? GetQuery(string text);

    /// <summary>
    /// The enabled effective skills matching <paramref name="query"/>: a project skill hides a user
    /// or built-in skill with the same ID, as when it runs. Recently used skills come first among
    /// equally good matches.
    /// </summary>
    IReadOnlyList<SkillCommandMatch> Match(IReadOnlyList<SkillDefinition> skills, string query,
        IReadOnlyList<string> recentIds);
}

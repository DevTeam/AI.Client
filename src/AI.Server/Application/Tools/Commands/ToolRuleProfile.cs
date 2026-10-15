namespace AI.Application.Tools.Commands;

/// <summary>What a profile needs to know about the project a call runs in.</summary>
public sealed record RuleContext(IReadOnlyList<CommandGrant> Grants);

/// <summary>
/// One call as rules see it, whatever the tool: what it does, in a form a person reads and a
/// pattern can match, and what stands against allowing it unasked.
/// </summary>
/// <param name="Display">What the call does, as the card shows it: <c>git log -5</c>, <c>write_file docs/a.md</c>.</param>
/// <param name="Warnings">One short English sentence each, shown on the card and to the risk check.</param>
/// <param name="BlocksAllow">Whether no Allow rule may let the call through unasked.</param>
/// <param name="ExactOnly">Whether only a pattern that spells this exact call may allow it.</param>
/// <param name="Options">The patterns the approval card offers to remember, the preselected one first.</param>
/// <param name="Value">The profile's own reading of the call, which its patterns match against.</param>
/// <param name="Arguments">
/// The arguments to run instead of the ones given, when the profile pins what it checked (the
/// program PATH found); null to run them as given.
/// </param>
/// <param name="Details">What the profile found, for the risk check; serialized as it is.</param>
public sealed record RuleSubject(
    string Display,
    IReadOnlyList<string> Warnings,
    bool BlocksAllow,
    bool ExactOnly,
    IReadOnlyList<string> Options,
    object Value,
    string? Arguments = null,
    object? Details = null);

/// <summary>A rule's pattern, read by the profile of the tool the rule decides.</summary>
public interface IRulePattern
{
    /// <summary>The pattern as stored, spelled the canonical way.</summary>
    string Text { get; }

    /// <summary>Whether it matches only the one call it spells.</summary>
    bool IsExact { get; }

    /// <summary>Orders patterns from least to most specific; compared only between patterns of one tool.</summary>
    (int Literal, int Closed, int Characters) Specificity { get; }

    /// <summary>
    /// Whether the call matches. A call can name several things (a move names two paths): an Allow or
    /// Ask must cover all of them, a Deny needs only one, which <paramref name="any"/> asks for.
    /// </summary>
    bool Matches(RuleSubject subject, bool any);
}

/// <summary>
/// What command rules need to know about one tool: how its patterns are written and how a call of
/// it reads. Deciding, storing, scoping and showing rules are the same for every tool.
/// </summary>
public interface IToolRuleProfile
{
    Guid ServerId { get; }
    string Tool { get; }

    bool TryParsePattern(string text, out IRulePattern? pattern, out string? problem);

    /// <summary>Null when the arguments say nothing a rule could match.</summary>
    Task<RuleSubject?> ReadAsync(string arguments, RuleContext context, CancellationToken cancellationToken);
}

/// <summary>The tools command rules can decide, one profile each.</summary>
public interface IToolRuleProfiles
{
    IReadOnlyList<IToolRuleProfile> All { get; }

    IToolRuleProfile? Find(string tool);

    IToolRuleProfile? Find(Guid serverId, string tool);
}

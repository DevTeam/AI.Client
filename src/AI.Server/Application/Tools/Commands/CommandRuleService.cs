namespace AI.Application.Tools.Commands;

using AI.Contracts.Settings;
using Projects;

/// <summary>What the command rules say about one call of a tool they decide.</summary>
/// <param name="Subject">The call as the tool's rule profile read it.</param>
/// <param name="Rule">The rule that decided, or null when none matched.</param>
/// <param name="Decision">
/// Allow, Ask or Deny from <paramref name="Rule"/>, or null when no rule matched and the tool's own
/// policy stands. An Allow the profile's guard does not let through is Ask here.
/// </param>
public sealed record CommandEvaluation(RuleSubject Subject, CommandRule? Rule, string? Decision);

public interface ICommandRuleService
{
    /// <summary>The global rules, and those of the project and the chat when they are given.</summary>
    Task<IReadOnlyList<CommandRule>> ListAsync(Guid? projectId, Guid? chatId, CancellationToken cancellationToken);

    /// <summary>Saves a rule; one with the same scope, owner and pattern is replaced.</summary>
    Task<CommandRule> AddAsync(CreateCommandRuleRequest request, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether command rules decide calls of this tool.</summary>
    bool Decides(Guid serverId, string tool);

    /// <summary>Null when rules do not decide the tool, or the arguments say nothing a rule could match.</summary>
    Task<CommandEvaluation?> EvaluateAsync(Guid projectId, Guid chatId, string tool, string arguments,
        CancellationToken cancellationToken);
}

/// <remarks>
/// <para>
/// The same for every tool; only reading a call and a pattern belongs to the tool's
/// <see cref="IToolRuleProfile"/>. A matching Deny always wins — a Deny matches when any thing the
/// call names does, an Allow or Ask only when all do. Otherwise the most specific matching pattern
/// decides, and between equally specific patterns the narrower scope does: chat, then project,
/// then global.
/// </para>
/// <para>
/// A call the profile marks exact-only (a shell, a command that can discard work, a deletion) is
/// allowed only by a rule that spells it exactly; a broader Allow does not reach it. An Allow never
/// lets through a call the profile's guard stops, such as one naming a path outside the project:
/// that call is asked about instead.
/// </para>
/// </remarks>
public sealed class CommandRuleService(ICommandRuleRepository repository, IProjectService projects,
    IToolRuleProfiles profiles, IIdGenerator ids, IClock clock) : ICommandRuleService
{
    private static readonly string[] Decisions = ["Allow", "Ask", "Deny"];

    public async Task<IReadOnlyList<CommandRule>> ListAsync(Guid? projectId, Guid? chatId, CancellationToken cancellationToken) =>
        (await repository.LoadAsync(cancellationToken)).Where(rule => Applies(rule, projectId, chatId)).ToArray();

    public async Task<CommandRule> AddAsync(CreateCommandRuleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Decisions.Contains(request.Decision, StringComparer.Ordinal))
            throw new ArgumentException("A command rule decides Allow, Ask or Deny.", nameof(request));
        var profile = profiles.Find(request.Tool)
            ?? throw new ArgumentException(
                $"Command rules can decide calls of {string.Join(", ", profiles.All.Select(item => item.Tool))} only.", nameof(request));
        var (projectId, chatId) = request.Scope switch
        {
            CommandRule.Global => ((Guid?)null, (Guid?)null),
            CommandRule.Project when request.ProjectId is { } project => (project, null),
            CommandRule.Chat when request is { ProjectId: { } project, ChatId: { } chat } => (project, chat),
            _ => throw new ArgumentException("A Project rule needs its project, and a Chat rule its project and chat.",
                nameof(request))
        };
        if (!profile.TryParsePattern(request.Pattern, out var pattern, out var problem))
            throw new ArgumentException(problem, nameof(request));
        var rule = new CommandRule(ids.Create(), request.Scope, projectId, chatId, pattern!.Text, request.Decision, clock.UtcNow,
            request.Tool);
        await repository.UpdateAsync(rules => rules
            .Where(item => !(item.Tool == rule.Tool && item.Scope == rule.Scope && item.ProjectId == rule.ProjectId
                             && item.ChatId == rule.ChatId && item.Pattern == rule.Pattern))
            .Append(rule)
            .ToArray(), cancellationToken);
        return rule;
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        var removed = false;
        await repository.UpdateAsync(rules =>
        {
            removed = rules.Any(rule => rule.Id == id);
            return rules.Where(rule => rule.Id != id).ToArray();
        }, cancellationToken);
        return removed;
    }

    public bool Decides(Guid serverId, string tool) => profiles.Find(serverId, tool) is not null;

    public async Task<CommandEvaluation?> EvaluateAsync(Guid projectId, Guid chatId, string tool, string arguments,
        CancellationToken cancellationToken)
    {
        if (profiles.Find(tool) is not { } profile) return null;
        var project = await projects.GetAsync(projectId, cancellationToken);
        var grants = project?.DirectoryGrants.Select(grant => new CommandGrant(grant.CanonicalRoot, grant.Recursive)).ToArray()
            ?? [];
        if (await profile.ReadAsync(arguments, new RuleContext(grants), cancellationToken) is not { } subject) return null;
        var matching = (await ListAsync(projectId, chatId, cancellationToken))
            .Where(rule => rule.Tool == tool)
            .Select(rule => (Rule: rule, Pattern: profile.TryParsePattern(rule.Pattern, out var pattern, out _) ? pattern : null))
            .Where(item => item.Pattern is not null && item.Pattern.Matches(subject, any: item.Rule.Decision == "Deny")
                           && (item.Rule.Decision != "Allow" || item.Pattern.IsExact || !subject.ExactOnly))
            .OrderByDescending(item => item.Rule.Decision == "Deny")
            .ThenByDescending(item => item.Pattern!.Specificity)
            .ThenByDescending(item => Rank(item.Rule.Scope))
            .ThenByDescending(item => item.Rule.CreatedAt)
            .Select(item => item.Rule)
            .FirstOrDefault();
        var decision = matching?.Decision is "Allow" && subject.BlocksAllow ? "Ask" : matching?.Decision;
        return new CommandEvaluation(subject, matching, decision);
    }

    private static bool Applies(CommandRule rule, Guid? projectId, Guid? chatId) => rule.Scope switch
    {
        CommandRule.Global => true,
        CommandRule.Project => projectId is not null && rule.ProjectId == projectId,
        CommandRule.Chat => projectId is not null && chatId is not null && rule.ProjectId == projectId && rule.ChatId == chatId,
        _ => false
    };

    private static int Rank(string scope) => scope switch
    {
        CommandRule.Chat => 3,
        CommandRule.Project => 2,
        _ => 1
    };
}

namespace AI.Application.Tools.Commands;

using AI.Contracts.FileSystem;
using AI.Contracts.Settings;
using Projects;

/// <summary>
/// What the command rules say about one <c>process_run</c> call.
/// </summary>
/// <param name="Rule">The rule that decided, or null when none matched.</param>
/// <param name="Decision">
/// Allow, Ask or Deny from <paramref name="Rule"/>, or null when no rule matched and the tool's own
/// policy stands. An Allow the guard does not let through is Ask here.
/// </param>
/// <param name="Options">The patterns the approval card may offer to remember, the default first.</param>
public sealed record CommandEvaluation(CommandCheck Check, CommandRule? Rule, string? Decision, IReadOnlyList<string> Options);

public interface ICommandRuleService
{
    /// <summary>The global rules, and those of the project and the chat when they are given.</summary>
    Task<IReadOnlyList<CommandRule>> ListAsync(Guid? projectId, Guid? chatId, CancellationToken cancellationToken);

    /// <summary>Saves a rule; one with the same scope, owner and pattern is replaced.</summary>
    Task<CommandRule> AddAsync(CreateCommandRuleRequest request, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Null when the arguments name no program.</summary>
    Task<CommandEvaluation?> EvaluateAsync(Guid projectId, Guid chatId, string arguments, CancellationToken cancellationToken);
}

/// <remarks>
/// <para>
/// A matching Deny always wins. Otherwise the most specific matching pattern decides — more words
/// matched exactly, then a pattern that ends where the command must — and between equally specific
/// patterns the narrower scope does: chat, then project, then global.
/// </para>
/// <para>
/// A shell, a launcher or a command that can discard work is allowed only by a rule that spells it
/// exactly; a broader Allow does not reach it. An Allow never lets through a call the guard stops,
/// such as one with a path outside the project: that call is asked about instead.
/// </para>
/// </remarks>
public sealed class CommandRuleService(ICommandRuleRepository repository, IProjectService projects,
    ICommandParser parser, IPath paths, IIdGenerator ids, IClock clock) : ICommandRuleService
{
    private static readonly string[] Decisions = ["Allow", "Ask", "Deny"];

    public async Task<IReadOnlyList<CommandRule>> ListAsync(Guid? projectId, Guid? chatId, CancellationToken cancellationToken) =>
        (await repository.LoadAsync(cancellationToken)).Where(rule => Applies(rule, projectId, chatId)).ToArray();

    public async Task<CommandRule> AddAsync(CreateCommandRuleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Decisions.Contains(request.Decision, StringComparer.Ordinal))
            throw new ArgumentException("A command rule decides Allow, Ask or Deny.", nameof(request));
        if (!CommandRule.Tools.Contains(request.Tool, StringComparer.Ordinal))
            throw new ArgumentException($"Command rules can decide calls of {string.Join(", ", CommandRule.Tools)} only.",
                nameof(request));
        var (projectId, chatId) = request.Scope switch
        {
            CommandRule.Global => ((Guid?)null, (Guid?)null),
            CommandRule.Project when request.ProjectId is { } project => (project, null),
            CommandRule.Chat when request is { ProjectId: { } project, ChatId: { } chat } => (project, chat),
            _ => throw new ArgumentException("A Project rule needs its project, and a Chat rule its project and chat.",
                nameof(request))
        };
        if (!CommandPattern.TryParse(request.Pattern, paths.IsCaseSensitive, out var pattern, out var error))
            throw new ArgumentException(error, nameof(request));
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

    public async Task<CommandEvaluation?> EvaluateAsync(Guid projectId, Guid chatId, string arguments,
        CancellationToken cancellationToken)
    {
        if (await parser.ParseAsync(arguments, cancellationToken) is not { } command) return null;
        var project = await projects.GetAsync(projectId, cancellationToken);
        var grants = project?.DirectoryGrants.Select(grant => new CommandGrant(grant.CanonicalRoot, grant.Recursive)).ToArray()
            ?? [];
        var check = CommandGuard.Check(command, grants, paths);
        var matching = (await ListAsync(projectId, chatId, cancellationToken))
            .Where(rule => rule.Tool == CommandRule.ProcessRun)
            .Select(rule => (Rule: rule, Pattern: CommandPattern.TryParse(rule.Pattern, paths.IsCaseSensitive, out var pattern, out _)
                ? pattern : null))
            .Where(item => item.Pattern is not null && item.Pattern.Matches(command)
                           && (item.Rule.Decision != "Allow" || item.Pattern.IsExact || !check.ExactOnly))
            .OrderByDescending(item => item.Rule.Decision == "Deny")
            .ThenByDescending(item => item.Pattern!.Specificity)
            .ThenByDescending(item => Rank(item.Rule.Scope))
            .ThenByDescending(item => item.Rule.CreatedAt)
            .Select(item => item.Rule)
            .FirstOrDefault();
        var decision = matching?.Decision is "Allow" && check.BlocksAllow ? "Ask" : matching?.Decision;
        return new CommandEvaluation(check, matching, decision, CommandGuard.Options(check));
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

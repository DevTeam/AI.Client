namespace AI.Application.Skills;

using AI.Contracts.Skills;
using Chat;
using Tools;

/// <summary>
/// The route for a new user message: the skills to run, in order, and the tools the first steps
/// need. <paramref name="ContinuesActive"/> says the message goes on with the playbook already in
/// progress rather than starting one.
/// </summary>
public sealed record SkillRoute(IReadOnlyList<SkillDefinition> Skills, IReadOnlyList<string> Tools, bool ContinuesActive);

/// <summary>Runs the <c>skill-route</c> skill for the message that starts a turn.</summary>
public interface ISkillRouting
{
    /// <summary>
    /// Null when the turn does not start with a new user message, when the user already picked a
    /// skill, or when routing is disabled, fails or times out: the turn then goes on unrouted.
    /// </summary>
    Task<SkillRoute?> RouteAsync(ToolRunContext run, IReadOnlyList<ChatCompletionMessage> context,
        IReadOnlyList<AgentTool> tools, CancellationToken cancellationToken);
}

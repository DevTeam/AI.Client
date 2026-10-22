namespace AI.Application.Skills;

using AI.Contracts.Skills;
using Chat;

/// <summary>
/// What the model is told about skills outside the skill tools themselves: the catalog it chooses
/// from before acting, and the playbook it is in the middle of, so that a follow-up message stays
/// with that playbook and a new task switches to another.
/// </summary>
public interface ISkillGuide
{
    /// <summary>
    /// The enabled skills in effect for the project, one per id: a Project skill hides a User one,
    /// and either hides a built-in with the same id. Sorted by id.
    /// </summary>
    Task<IReadOnlyList<SkillDefinition>> EffectiveAsync(Guid? projectId, CancellationToken cancellationToken);

    /// <summary>
    /// The latest playbook loaded in the conversation within the last few user turns, or null.
    /// </summary>
    Task<SkillDefinition?> ActivePlaybookAsync(Guid projectId, IReadOnlyList<ChatCompletionMessage> context,
        CancellationToken cancellationToken);
}

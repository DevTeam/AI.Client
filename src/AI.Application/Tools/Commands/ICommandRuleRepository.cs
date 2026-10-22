namespace AI.Application.Tools.Commands;

using AI.Contracts.Settings;

/// <summary>Every command rule of every scope, kept in one document beside the global settings.</summary>
public interface ICommandRuleRepository
{
    Task<IReadOnlyList<CommandRule>> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Replaces the rules with what <paramref name="update"/> makes of them, one writer at a time.</summary>
    Task<IReadOnlyList<CommandRule>> UpdateAsync(Func<IReadOnlyList<CommandRule>, IReadOnlyList<CommandRule>> update,
        CancellationToken cancellationToken);
}

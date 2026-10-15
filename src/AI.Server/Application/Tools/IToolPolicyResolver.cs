namespace AI.Application.Tools;

/// <summary>
/// Works out whether a tool may run, asks first, or is refused, from the policies stacked at the
/// chat, project and global levels and the server's own switch.
/// </summary>
public interface IToolPolicyResolver
{
    Task<EffectiveToolPolicy> ResolveAsync(
        Guid projectId, Guid chatId, Guid serverId, string name, string schemaHash, CancellationToken cancellationToken);

    Task<EffectiveToolPolicy> ResolveAsync(Guid projectId, Guid chatId, Guid branchId,
        Guid serverId, string name, string schemaHash, CancellationToken cancellationToken) =>
        ResolveAsync(projectId, chatId, serverId, name, schemaHash, cancellationToken);

    /// <summary>
    /// The policy for one call: the tool's own, refined by the command rules when the call is a
    /// <c>process_run</c> and <paramref name="arguments"/> are given.
    /// </summary>
    Task<EffectiveToolPolicy> ResolveAsync(Guid projectId, Guid chatId, Guid branchId,
        Guid serverId, string name, string schemaHash, string? arguments, CancellationToken cancellationToken) =>
        ResolveAsync(projectId, chatId, branchId, serverId, name, schemaHash, cancellationToken);
}

namespace AI.Client.Application.Tools;

/// <summary>
/// Works out whether a tool may run, asks first, or is refused, from the policies stacked at the
/// chat, project and global levels and the server's own switch.
/// </summary>
public interface IToolPolicyResolver
{
    Task<EffectiveToolPolicy> ResolveAsync(
        Guid projectId, Guid chatId, Guid serverId, string name, string schemaHash, CancellationToken cancellationToken);
}

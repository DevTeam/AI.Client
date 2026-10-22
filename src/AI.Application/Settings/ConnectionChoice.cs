namespace AI.Application.Settings;

using AI.Contracts.Settings;

/// <summary>
/// Which model connection answers a chat. The composer shows the chat's connection, else the
/// project's, else the default, and passes over a disabled one; a request has to go to the same
/// place, or the chat says one model and fails on another.
/// </summary>
public interface IConnectionChoice
{
    /// <summary>
    /// The first <em>enabled</em> connection among <paramref name="preferred"/>, in order, then the
    /// enabled default, then any enabled one. Null when none is enabled.
    /// </summary>
    ConnectionSettings? Choose(IReadOnlyList<ConnectionSettings> connections, params Guid?[] preferred);
}

public sealed class ConnectionChoice : IConnectionChoice
{
    public ConnectionSettings? Choose(IReadOnlyList<ConnectionSettings> connections, params Guid?[] preferred)
    {
        var enabled = connections.Where(item => item.Enabled).ToArray();
        return preferred.Select(id => enabled.FirstOrDefault(item => item.Id == id)).FirstOrDefault(item => item is not null)
               ?? enabled.FirstOrDefault(item => item.IsDefault)
               ?? enabled.FirstOrDefault();
    }
}

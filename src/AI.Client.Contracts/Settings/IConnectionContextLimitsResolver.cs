namespace AI.Client.Contracts.Settings;

public interface IConnectionContextLimitsResolver
{
    ResolvedConnectionContextLimits Resolve(ConnectionSettings? connection);
}

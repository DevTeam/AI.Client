namespace AI.Contracts.Settings;

public interface IConnectionContextLimitsResolver
{
    ResolvedConnectionContextLimits Resolve(ConnectionSettings? connection);
}

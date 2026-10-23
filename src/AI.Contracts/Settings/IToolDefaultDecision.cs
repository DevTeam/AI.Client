namespace AI.Contracts.Settings;

/// <summary>The decision used when no saved rule matches a tool's current schema.</summary>
public interface IToolDefaultDecision
{
    string GetDecision(Guid serverId, string name);
}

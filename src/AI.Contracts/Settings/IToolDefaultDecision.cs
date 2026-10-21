namespace AI.Contracts.Settings;

/// <summary>The decision used when no saved rule matches a tool's current schema.</summary>
public interface IToolDefaultDecision
{
    /// <param name="sandbox">
    /// The sandbox settings in effect, which decide the default of a tool that is only safe to run
    /// unasked inside the sandbox; <c>null</c> means the defaults of <see cref="ToolSandboxSettings"/>.
    /// </param>
    string GetDecision(Guid serverId, string name, ToolSandboxSettings? sandbox = null);
}

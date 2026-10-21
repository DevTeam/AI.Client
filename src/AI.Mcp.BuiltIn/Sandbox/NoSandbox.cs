namespace AI.Mcp.BuiltIn.Sandbox;

using AI.Sandbox;

/// <summary>The sandbox switched off, for a runner constructed outside the composition.</summary>
internal sealed class NoSandbox : IToolSandbox
{
    public static readonly NoSandbox Instance = new();

    public SandboxPolicy? Policy => null;

    public string? TemporaryDirectory => null;

    public string Describe() => "The program is not sandboxed: it can read and write anything the host user can. ";
}

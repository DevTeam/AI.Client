namespace AI.Mcp.BuiltIn.Grants;

public sealed record DirectoryGrantSpec(string Root, bool Recursive, IReadOnlySet<GrantCapability> Capabilities);

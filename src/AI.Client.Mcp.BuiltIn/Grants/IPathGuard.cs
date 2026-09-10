namespace AI.Client.Mcp.BuiltIn.Grants;

public interface IPathGuard
{
    IReadOnlyList<DirectoryGrantSpec> Grants { get; }

    /// <summary>Canonicalizes <paramref name="path"/> and throws <see cref="GrantException"/> unless a grant allows <paramref name="capability"/> there.</summary>
    string Resolve(string path, GrantCapability capability);
}

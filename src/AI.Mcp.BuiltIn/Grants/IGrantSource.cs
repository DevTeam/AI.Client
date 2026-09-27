namespace AI.Mcp.BuiltIn.Grants;

public interface IGrantSource
{
    IReadOnlyList<DirectoryGrantSpec> Load();
}

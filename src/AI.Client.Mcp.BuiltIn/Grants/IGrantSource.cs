namespace AI.Client.Mcp.BuiltIn.Grants;

public interface IGrantSource
{
    IReadOnlyList<DirectoryGrantSpec> Load();
}

namespace AI.Client.Mcp.BuiltIn.Grants;

public sealed class GrantException : Exception
{
    public GrantException(string message) : base(message)
    {
    }

    public GrantException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public GrantException()
    {
    }
}

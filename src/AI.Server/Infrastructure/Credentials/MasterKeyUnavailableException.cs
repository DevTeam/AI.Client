namespace AI.Infrastructure.Credentials;

public sealed class MasterKeyUnavailableException : Exception
{
    public MasterKeyUnavailableException()
    {
    }

    public MasterKeyUnavailableException(string message) : base(message)
    {
    }

    public MasterKeyUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

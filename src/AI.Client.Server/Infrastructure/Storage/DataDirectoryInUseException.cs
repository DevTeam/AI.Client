namespace AI.Client.Infrastructure.Storage;

public sealed class DataDirectoryInUseException : IOException
{
    public DataDirectoryInUseException()
    {
    }

    public DataDirectoryInUseException(string message) : base(message)
    {
    }

    public DataDirectoryInUseException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

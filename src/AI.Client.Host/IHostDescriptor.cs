namespace AI.Client.Host;

public interface IHostDescriptor
{
    string ProductName { get; }

    string Version { get; }
}

namespace AI.Client.Server.Hosting;

public interface IHostDescriptor
{
    string ProductName { get; }

    string Version { get; }
}

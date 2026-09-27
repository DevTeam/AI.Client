namespace AI.Server.Hosting;

public interface IHostDescriptor
{
    string ProductName { get; }

    string Version { get; }
}

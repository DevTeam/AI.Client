namespace AI.Server.Hosting;

using AI.Contracts;

public sealed class HostDescriptor : IHostDescriptor
{
    public string ProductName => HostProtocol.ProductName;

    public string Version => typeof(HostDescriptor).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}

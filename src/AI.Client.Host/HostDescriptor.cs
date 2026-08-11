namespace AI.Client.Host;

internal sealed class HostDescriptor : IHostDescriptor
{
    public string ProductName => "AI.Client";

    public string Version => typeof(HostDescriptor).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}

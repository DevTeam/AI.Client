namespace AI.Infrastructure.Tools;

using ModelContextProtocol.Client;

/// <summary>Tool server processes as children of the Host on this computer.</summary>
public sealed class LocalToolEnvironment : IToolEnvironment
{
    public IClientTransport Launch(ToolProcessLaunch launch)
    {
        var options = new StdioClientTransportOptions
        {
            Name = launch.Name, Command = launch.Command, Arguments = launch.Arguments.ToArray(),
            WorkingDirectory = launch.WorkingDirectory, EnvironmentVariables = launch.Environment,
            ShutdownTimeout = launch.ShutdownTimeout
        };
        if (launch.InheritEnvironment is { } inherit) options.InheritEnvironmentVariables = inherit;
        return new StdioClientTransport(options);
    }
}

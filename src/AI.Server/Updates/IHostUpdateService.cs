namespace AI.Updates;

using Microsoft.Extensions.Hosting;

public interface IHostUpdateService : IHostedService, IDisposable
{
    IUpdateManager Manager { get; }
    Action? Shutdown { get; set; }
}

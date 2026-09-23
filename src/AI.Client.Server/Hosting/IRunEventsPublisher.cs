namespace AI.Client.Server.Hosting;

using Microsoft.AspNetCore.Http;

/// <summary>The server-sent event stream the UI keeps open to follow runs and data changes.</summary>
public interface IRunEventsPublisher
{
    Task WriteAsync(HttpResponse response, CancellationToken cancellationToken);
}

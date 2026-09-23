namespace AI.Client.Server.Hosting;

using Microsoft.AspNetCore.Builder;

/// <summary>Serves the Blazor UI from the same origin as the API.</summary>
public interface IWebClientHost
{
    void Configure(WebApplicationBuilder builder);

    void Map(WebApplication app);
}

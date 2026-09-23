namespace AI.Client.Server.Hosting;

using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

public sealed class WebClientHost : IWebClientHost
{
    public void Configure(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // A published app carries the UI in wwwroot; a build output only has the manifest that
        // points at the Web project's files. This reads the manifest when there is one and does
        // nothing otherwise, so both run the same way whatever the environment is called.
        builder.WebHost.UseStaticWebAssets();
    }

    public void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // The UI reads the API address from appsettings*.json. The copies in wwwroot point at the
        // Web dev server's backend; served from here, the API is this very origin, whichever port
        // it got. Answering those files ourselves keeps that true in every environment.
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value;
            if (HttpMethods.IsGet(context.Request.Method)
                && path is not null
                && path.StartsWith("/appsettings", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && path.IndexOf('/', 1) < 0)
            {
                var origin = $"{context.Request.Scheme}://{context.Request.Host}/";
                context.Response.ContentType = "application/json";
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ApiBaseUrl = origin }), context.RequestAborted);
                return;
            }

            await next(context);
        });

        // MapStaticAssets serves _framework itself, with the precompressed variants it negotiates.
        // UseBlazorFrameworkFiles must not be added as well: it rewrites the same requests to .gz
        // paths that the static asset endpoints then refuse.
        app.MapStaticAssets();
        // Unknown API routes stay 404: falling back to index.html would answer a typo with a page
        // and a 200, and the client would try to parse HTML as JSON.
        app.Map("/api/{**path}", () => Results.NotFound());
        app.MapFallbackToFile("index.html");
    }
}

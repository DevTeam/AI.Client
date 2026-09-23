namespace AI.Client.Server.Hosting;

using Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class AiClientServer(
    ServerOptions options,
    ILoggerProvider fileLogger,
    IApiExceptionHandler exceptionHandler,
    IEnumerable<IEndpointModule> endpoints) : IAiClientServer
{
    public async Task RunAsync(IServiceProviderFactory<IServiceCollection> services, CancellationToken cancellationToken)
    {
        // The command line has already been parsed into `options`; ASP.NET gets no arguments, so
        // there is exactly one parser. appsettings.json and environment variables still apply.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        if (options.Urls is { } urls)
        {
            builder.WebHost.UseUrls(urls);
        }

        // The file logger comes from the same composition as the repositories, so logs and data
        // are rooted in the same directory by construction.
        builder.Logging.AddProvider(fileLogger);
        builder.Services.AddHostedService<ChatRunHostedService>();
        builder.Host.UseServiceProviderFactory(services);

        // The frontend runs in a separate process on its own origin (default http://localhost:52174),
        // so every API request needs an explicit CORS allowlist. Origins are listed in appsettings.json
        // under `Cors:AllowedOrigins`; defaults come from appsettings.Development.json. We deliberately
        // avoid `AllowAnyOrigin` — the SPA needs a real origin so credentials and SSE can be added later
        // without rewriting the policy.
        var corsOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        builder.Services.AddCors(cors =>
        {
            cors.AddDefaultPolicy(policy => policy
                .WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithExposedHeaders("Content-Disposition"));
        });

        var app = builder.Build();
        app.Use(exceptionHandler.InvokeAsync);

        // CORS runs before endpoint routing so preflight OPTIONS requests are handled before any
        // endpoint binding refuses to route them. WebApplication wires the rest of the pipeline itself.
        app.UseCors();

        foreach (var module in endpoints)
        {
            module.Map(app);
        }

        await app.RunAsync(cancellationToken);
    }
}

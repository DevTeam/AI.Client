namespace AI.Server.Hosting;

using Endpoints;
using Infrastructure.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class AiClientServer(
    ServerOptions options,
    IDataDirectoryLock dataDirectoryLock,
    ILoggerProvider fileLogger,
    IApiExceptionHandler exceptionHandler,
    IWebClientHost webClient,
    IEnumerable<IEndpointModule> endpoints) : IAiClientServer
{
    public async Task<IRunningServer> StartAsync(IServiceProviderFactory<IServiceCollection> services, CancellationToken cancellationToken)
    {
        // Taken first and held for the server's lifetime: nothing may read or write the data
        // directory before it is certain that no other process does.
        var directoryLock = dataDirectoryLock.Acquire();
        try
        {
            var app = Build(services);
            await app.StartAsync(cancellationToken);
            return new RunningServer(app, directoryLock);
        }
        catch
        {
            directoryLock.Dispose();
            throw;
        }
    }

    private WebApplication Build(IServiceProviderFactory<IServiceCollection> services)
    {
        // The command line has already been parsed into `options`; ASP.NET gets no arguments, so
        // there is exactly one parser. appsettings.json and environment variables still apply.
        // Content lives next to the executable, not in whatever directory it was started from: a
        // desktop app launched from a shortcut has an arbitrary working directory.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory
        });
        if (options.Urls is { } urls)
        {
            builder.WebHost.UseUrls(urls);
        }

        if (options.ServeWeb)
        {
            webClient.Configure(builder);
        }

        if (!options.StopOnProcessSignals)
        {
            builder.Services.AddSingleton<IHostLifetime, EmbeddedHostLifetime>();
        }

        // The file logger comes from the same composition as the repositories, so logs and data
        // are rooted in the same directory by construction.
        builder.Logging.AddProvider(fileLogger);
        builder.Services.AddHostedService<ChatRunHostedService>();
        builder.Host.UseServiceProviderFactory(services);

        // The frontend's dev server runs on its own origin (default http://localhost:52174), so
        // every API request from it needs an explicit CORS allowlist. Origins are listed in
        // appsettings.json under `Cors:AllowedOrigins`; defaults come from
        // appsettings.Development.json. A UI served by this server shares its origin and needs none.
        // We deliberately avoid `AllowAnyOrigin` — the SPA needs a real origin so credentials and
        // SSE can be added later without rewriting the policy.
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

        if (options.ServeWeb)
        {
            webClient.Map(app);
        }

        return app;
    }

    private sealed class RunningServer(WebApplication app, IDisposable directoryLock) : IRunningServer
    {
        public Uri Address { get; } = new(app.Urls.First());

        public Task WaitForShutdownAsync(CancellationToken cancellationToken) => app.WaitForShutdownAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            try
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
            finally
            {
                directoryLock.Dispose();
            }
        }
    }
}

namespace AI.Server.Hosting.Endpoints;

using AI.Application.Runs;
using AI.Contracts.Runs;
using AI.Contracts.Updates;
using AI.Updates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public sealed class UpdateEndpoints(ServerOptions options) : IEndpointModule
{
    public void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/updates");
        // Updaters are local-only. A cross-origin page cannot trigger installation even when
        // this Host was started without the public-Web bridge middleware.
        group.AddEndpointFilter(async (context, next) =>
        {
            var request = context.HttpContext.Request;
            var remote = context.HttpContext.Connection.RemoteIpAddress;
            var origin = request.Headers.Origin.ToString();
            if (remote is not null && !System.Net.IPAddress.IsLoopback(remote)
                || request.Headers["Sec-Fetch-Site"] == "cross-site" && origin.Length == 0
                || origin.Length > 0 && origin != $"{request.Scheme}://{request.Host}"
                    && !(options.PublicWeb && origin == options.PublicOrigin.TrimEnd('/')))
                return Results.StatusCode(403);
            return await next(context);
        });
        group.MapGet("", (IHostUpdateService updates) => updates.Manager.State);
        group.MapPut("/preferences", (UpdatePreferences preferences, IHostUpdateService updates, CancellationToken token) =>
            updates.Manager.ExecuteAsync("preferences", preferences, token));
        group.MapPost("/{operation}", (string operation, IHostUpdateService updates, CancellationToken token) =>
            updates.Manager.ExecuteAsync(operation, null, token));
        group.MapPost("/desktop-idle", async (IChatRunDispatcher runs, CancellationToken token) =>
        {
            if (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "AI.Desktop")
                return runs.TryEnterUpdateMaintenance();
            // A Desktop using a separate Host only closes its window. The Host stays running.
            var snapshots = await runs.GetSnapshotAsync(token);
            return snapshots.All(run => run.Status != ChatRunStatus.Generating
                && run.PendingApproval is null && run.PendingPrompt is null && run.ActiveTools is not { Count: > 0 });
        });
        group.MapPost("/desktop-resume", (IChatRunDispatcher runs) => { runs.LeaveUpdateMaintenance(); return Results.NoContent(); });
    }
}

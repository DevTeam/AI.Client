namespace AI.Server.Hosting.Endpoints;

using Application.Settings;
using Application.Tools;
using Contracts.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public sealed class SettingsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet(
            "/api/settings",
            (IGlobalSettingsService service, CancellationToken cancellationToken) => service.GetAsync(cancellationToken));

        routes.MapPut(
            "/api/settings",
            (SaveGlobalSettingsRequest request, IGlobalSettingsService service, CancellationToken cancellationToken) =>
                service.SaveAsync(request, cancellationToken));

        routes.MapPut(
            "/api/settings/chat-automation",
            (ChatAutomationSettings request, IGlobalSettingsService service, CancellationToken cancellationToken) =>
                service.SetChatAutomationAsync(request, cancellationToken));

        routes.MapPut(
            "/api/settings/connections/{id:guid}/credential",
            async (Guid id, UpdateSecretRequest request, IGlobalSettingsService service, CancellationToken cancellationToken) =>
                await service.SetConnectionCredentialAsync(id, request.Value, cancellationToken)
                    ? Results.NoContent() : Results.NotFound());

        routes.MapPut(
            "/api/settings/mcp/{id:guid}/credential",
            async (Guid id, UpdateSecretRequest request, IGlobalSettingsService service, CancellationToken cancellationToken) =>
                await service.SetMcpCredentialAsync(id, request.Value, cancellationToken)
                    ? Results.NoContent() : Results.NotFound());

        routes.MapDelete("/api/settings/mcp/{serverId:guid}/tool-policies",
            (Guid serverId, string name, string schemaHash, IGlobalSettingsService service, CancellationToken token) =>
                service.RemoveToolPolicyAsync(serverId, name, schemaHash, token));

        // Model catalog for the connection editor. The editor sends the Base URL (and a key) as typed,
        // since it is usually ahead of what is saved; the saved key stays on the Host. A POST because
        // the body may carry a key, which must not end up in a URL.
        routes.MapPost("/api/settings/connections/{id:guid}/models",
            (Guid id, ResolveConnectionModelsRequest request, IGlobalSettingsService service, CancellationToken token) =>
                service.ResolveConnectionModelsAsync(id, request, token));

        routes.MapGet("/api/mcp/default/tools", async (IToolSessionFactory factory, CancellationToken token) =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            // Discovery lists what every Host-provided server declares, regardless of whether a project
            // has it switched on: the settings UI is where it gets switched on, and it needs the tools to
            // show first. Grants stay empty, so file system tools remain fail-closed here.
            await using var session = await factory.OpenAsync([],
                new HashSet<Guid> { DefaultMcpServer.Id, AppMcpServer.Id },
                ToolRunContext.None, timeout.Token);
            return session.Tools.Select(tool => new McpToolInfo(tool.ServerId, tool.OriginalName, tool.ModelDefinition.Description, tool.SchemaHash)).ToArray();
        });
    }
}

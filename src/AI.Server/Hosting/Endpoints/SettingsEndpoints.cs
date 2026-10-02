namespace AI.Server.Hosting.Endpoints;

using Application.Settings;
using Application.Tools;
using Contracts.Settings;
using AI.Infrastructure.Tools;
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
                new HashSet<Guid> { DefaultMcpServer.Id, AppMcpServer.Id, CSharpMcpServer.Id },
                ToolRunContext.None, timeout.Token);
            return session.Tools.Select(tool => new McpToolInfo(tool.ServerId, tool.OriginalName, tool.ModelDefinition.Description, tool.SchemaHash)).ToArray();
        });

        routes.MapPost("/api/mcp/tools/discover", async (DiscoverMcpToolsRequest request,
            IToolSessionFactory factory, ExternalToolSessionFactory external, CancellationToken token) =>
        {
            if (!request.Server.Enabled || request.Server.Policy == "Deny")
                return Results.Problem("Enable the server and choose Ask or Allow before discovery.", statusCode: 400);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var id = request.Server.Id;
                await using var session = id == DefaultMcpServer.Id || id == AppMcpServer.Id || id == CSharpMcpServer.Id
                    ? await factory.OpenAsync([], new HashSet<Guid> { id }, ToolRunContext.None, timeout.Token)
                    : await external.OpenAsync(request.Server, timeout.Token, request.Credential);
                return Results.Ok(session.Tools.Select(tool => new McpToolInfo(
                    tool.ServerId, tool.OriginalName, tool.ModelDefinition.Description, tool.SchemaHash)).ToArray());
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return Results.Problem("MCP tool discovery timed out after 15 seconds.", statusCode: 504);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return Results.Problem($"Could not discover MCP tools: {error.Message}", statusCode: 400);
            }
        });
    }
}

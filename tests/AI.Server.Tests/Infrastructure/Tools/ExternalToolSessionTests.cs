namespace AI.Infrastructure.Tests.Tools;

using AI.Application.Settings;
using AI.Application.Tools;
using AI.Contracts.Settings;
using AI.Contracts.Tools;
using AI.Infrastructure.Tools;
using AI.Server.Hosting.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;
using System.Text.Json;
using System.Net.Http.Json;
using Xunit;

public sealed class ExternalToolSessionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DiscoveryEndpointUsesUnsavedConfigurationWithoutSavingOrCallingTools()
    {
        var repository = new Mock<IGlobalSettingsRepository>(MockBehavior.Strict);
        var external = new ExternalToolSessionFactory(Mock.Of<IGlobalSecretStore>(), new ToolResultModelProjector());
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IGlobalSettingsService>(Mock.Of<IGlobalSettingsService>());
        builder.Services.AddSingleton<IToolSessionFactory>(new CompositeToolSessionFactory([], repository.Object, external));
        builder.Services.AddSingleton(external);
        await using var app = builder.Build();
        new SettingsEndpoints().Map(app);
        await app.StartAsync(Token);
        var address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        using var http = new HttpClient { BaseAddress = new Uri(address) };
        var directory = Path.Combine(AppContext.BaseDirectory, "mcp-csharp");
        var server = Server("Stdio", "AI.Mcp.CSharp" + (OperatingSystem.IsWindows() ? ".exe" : ""), directory);
        using var response = await http.PostAsJsonAsync("/api/mcp/tools/discover", new DiscoverMcpToolsRequest(server), Token);
        response.EnsureSuccessStatusCode();
        var tools = await response.Content.ReadFromJsonAsync<McpToolInfo[]>(Token);
        tools.ShouldNotBeNull().ShouldHaveSingleItem().Name.ShouldBe("cs_run");
        repository.VerifyNoOtherCalls();

        using var denied = await http.PostAsJsonAsync("/api/mcp/tools/discover",
            new DiscoverMcpToolsRequest(server with { Policy = "Deny" }), Token);
        denied.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
        using var invalid = await http.PostAsJsonAsync("/api/mcp/tools/discover",
            new DiscoverMcpToolsRequest(server with { WorkingDirectory = "relative-directory" }), Token);
        invalid.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync(Token)).ShouldContain("existing absolute directory");
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task ConnectsConfiguredStdioServersAndRoutesIdenticalToolNames()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "mcp-csharp");
        var command = "AI.Mcp.CSharp" + (OperatingSystem.IsWindows() ? ".exe" : "");
        File.Exists(Path.Combine(directory, command)).ShouldBeTrue();
        var first = Server("Stdio", command, directory);
        var second = first with { Id = Guid.NewGuid(), Name = "Second CSharp" };
        var repository = new Mock<IGlobalSettingsRepository>();
        repository.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSettings([], [first, second], []));
        var secrets = new Mock<IGlobalSecretStore>();
        secrets.Setup(item => item.GetAsync("mcp-env", first.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"SCRIPT_TEST_VALUE\":\"saved-value\"}");
        first = first with { EnvironmentVariables = [new("SCRIPT_TEST_VALUE", null, true, true)] };
        repository.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSettings([], [first, second], []));
        var factory = new CompositeToolSessionFactory([], repository.Object,
            new ExternalToolSessionFactory(secrets.Object, new ToolResultModelProjector()));

        await using var session = await factory.OpenAsync([], new HashSet<Guid> { first.Id, second.Id },
            ToolRunContext.None, Token);
        session.Tools.Count.ShouldBe(2);
        session.Tools.Select(tool => tool.ModelDefinition.Name).Distinct().Count().ShouldBe(2);
        session.Tools.ShouldAllBe(tool => tool.OriginalName == "cs_run");
        var tool = session.Tools.Single(tool => tool.ServerId == first.Id);
        var result = await session.CallAsync(tool,
            "{\"code\":\"Environment.GetEnvironmentVariable(\\\"SCRIPT_TEST_VALUE\\\")\"}", null, Token);
        result.IsError.ShouldBeFalse();
        result.StructuredContent!.Value.GetProperty("returnValue").GetString().ShouldBe("saved-value");

        var catalog = new ToolCatalogRegistry();
        using var scope = catalog.Begin(ToolRunContext.None);
        catalog.Update(ToolRunContext.None, session.Tools);
        catalog.SearchAndPin(ToolRunContext.None, "C# script", 5).Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectsHttpAndUsesSavedOrEnteredCredential(bool entered)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var headers = new List<string>();
        var originalName = "probe." + new string('a', 58);
        app.MapPost("/mcp", async (HttpContext context) =>
        {
            headers.Add(context.Request.Headers.Authorization.ToString());
            using var message = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: Token);
            var request = message.RootElement;
            if (!request.TryGetProperty("id", out var id))
            {
                context.Response.StatusCode = 202;
                return;
            }
            // The SDK probes the newer stateless handshake before falling back to initialize.
            if (request.GetProperty("method").GetString() == "server/discover")
            {
                await context.Response.WriteAsJsonAsync(new { jsonrpc = "2.0", id,
                    error = new { code = -32601, message = "Method not found" } }, Token);
                return;
            }
            object result = request.GetProperty("method").GetString() switch
            {
                "initialize" => new { protocolVersion = "2025-11-25", capabilities = new { tools = new { } },
                    serverInfo = new { name = "Http probe", version = "1.0" } },
                "tools/list" => new { tools = new[] { new { name = originalName, description = "HTTP probe",
                    inputSchema = new { type = "object", properties = new { } } } } },
                "tools/call" => new { content = new[] { new { type = "text", text = "HTTP works" } } },
                _ => throw new InvalidOperationException("Unexpected MCP request")
            };
            if (request.GetProperty("method").GetString() == "tools/call")
                request.GetProperty("params").GetProperty("name").GetString().ShouldBe(originalName);
            await context.Response.WriteAsJsonAsync(new { jsonrpc = "2.0", id, result }, Token);
        });
        await app.StartAsync(Token);
        var address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        var server = Server("StreamableHttp") with { Url = address + "/mcp" };
        var secrets = new Mock<IGlobalSecretStore>();
        secrets.Setup(item => item.GetAsync("mcp", server.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("saved-token");
        var factory = new ExternalToolSessionFactory(secrets.Object, new ToolResultModelProjector());
        await using (var session = await factory.OpenAsync(server, Token, entered ? "entered-token" : null))
        {
            var tool = session.Tools.ShouldHaveSingleItem();
            tool.OriginalName.ShouldBe(originalName);
            tool.ModelDefinition.Name.Length.ShouldBeLessThanOrEqualTo(64);
            tool.ModelDefinition.Name.ShouldNotContain('.');
            var result = await session.CallAsync(tool, "{}", null, Token);
            result.ModelContent.ShouldContain("HTTP works");
        }
        headers.ShouldNotBeEmpty();
        headers.ShouldAllBe(header => header == (entered ? "Bearer entered-token" : "Bearer saved-token"));
        await app.StopAsync(Token);
    }

    [Theory]
    [InlineData(false, "Ask")]
    [InlineData(true, "Deny")]
    public async Task DoesNotStartDisabledOrDeniedServers(bool enabled, string policy)
    {
        var server = Server("Stdio", "does-not-exist") with { Enabled = enabled, Policy = policy };
        var factory = new ExternalToolSessionFactory(Mock.Of<IGlobalSecretStore>(), new ToolResultModelProjector());
        var error = await Should.ThrowAsync<InvalidOperationException>(() => factory.OpenAsync(server, Token));
        error.Message.ShouldContain("not started");
    }

    private static McpServerSettings Server(string transport, string? command = null, string? directory = null) =>
        new(Guid.NewGuid(), "CSharp", transport, true, "Ask", null, command, [], directory, [], false);
}

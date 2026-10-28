using AI.Contracts.FileSystem;
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

[Trait("Category", "Integration")]
public sealed class ExternalToolSessionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DiscoveryEndpointUsesUnsavedConfigurationWithoutSavingOrCallingTools()
    {
        var repository = new Mock<IGlobalSettingsRepository>(MockBehavior.Strict);
        var external = NewFactory(Mock.Of<IGlobalSecretStore>(), new ToolResultModelProjector());
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IGlobalSettingsService>(Mock.Of<IGlobalSettingsService>());
        builder.Services.AddSingleton<AI.Application.Settings.IConnectionImageProbe>(Mock.Of<AI.Application.Settings.IConnectionImageProbe>());
        await using var pool = NewPool(Mock.Of<IGlobalSecretStore>());
        builder.Services.AddSingleton<IToolSessionFactory>(NewComposite(repository.Object, Mock.Of<IGlobalSecretStore>(), pool, new ToolLists()));
        builder.Services.AddSingleton<IExternalToolSessionFactory>(external);
        await using var app = builder.Build();
        new SettingsEndpoints().Map(app);
        await app.StartAsync(Token);
        var address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        using var http = new HttpClient { BaseAddress = new Uri(address) };
        var server = Server("Stdio", BuiltInCommand, BuiltInDirectory);
        using var response = await http.PostAsJsonAsync("/api/mcp/tools/discover", new DiscoverMcpToolsRequest(server), Token);
        response.EnsureSuccessStatusCode();
        var tools = await response.Content.ReadFromJsonAsync<McpToolInfo[]>(Token);
        tools.ShouldNotBeNull().ShouldContain(tool => tool.Name == "cs_run");
        tools.ShouldAllBe(tool => tool.ServerId == server.Id);
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
        // The built-in server's executable, configured twice, stands in for two external stdio servers
        // that declare the same tool names.
        File.Exists(Path.Combine(BuiltInDirectory, BuiltInCommand)).ShouldBeTrue();
        var first = Server("Stdio", BuiltInCommand, BuiltInDirectory);
        var second = first with { Id = Guid.NewGuid(), Name = "Second external" };
        var repository = new Mock<IGlobalSettingsRepository>();
        repository.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSettings([], [first, second], []));
        var secrets = new Mock<IGlobalSecretStore>();
        secrets.Setup(item => item.GetAsync("mcp-env", first.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"SCRIPT_TEST_VALUE\":\"saved-value\"}");
        first = first with { EnvironmentVariables = [new("SCRIPT_TEST_VALUE", null, true, true)] };
        repository.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSettings([], [first, second], []));
        await using var pool = NewPool(secrets.Object);
        var factory = NewComposite(repository.Object, secrets.Object, pool, new ToolLists());

        await using var session = await factory.OpenAsync([], new HashSet<Guid> { first.Id, second.Id },
            ToolRunContext.None, Token);
        var scripts = session.Tools.Where(tool => tool.OriginalName == "cs_run").ToArray();
        scripts.Length.ShouldBe(2);
        scripts.Select(tool => tool.ServerId).ShouldBe([first.Id, second.Id], ignoreOrder: true);
        session.Tools.Select(tool => tool.ModelDefinition.Name).Distinct().Count().ShouldBe(session.Tools.Count);
        var tool = scripts.Single(tool => tool.ServerId == first.Id);
        var result = await session.CallAsync(tool,
            "{\"code\":\"Environment.GetEnvironmentVariable(\\\"SCRIPT_TEST_VALUE\\\")\"}", null, Token);
        result.IsError.ShouldBeFalse();
        result.StructuredContent!.Value.GetProperty("returnValue").GetString().ShouldBe("saved-value");

        var catalog = new ToolCatalogRegistry();
        using var scope = catalog.Begin(ToolRunContext.None);
        catalog.Update(ToolRunContext.None, session.Tools);
        var matches = catalog.SearchAndPin(ToolRunContext.None, "C# script", 10).Tools.Select(match => match.Name).ToArray();
        foreach (var script in scripts) matches.ShouldContain(script.ModelDefinition.Name);
    }

    private static string BuiltInDirectory => Path.Combine(AppContext.BaseDirectory, "mcp");

    private static string BuiltInCommand => "AI.Mcp.BuiltIn" + (OperatingSystem.IsWindows() ? ".exe" : "");

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
                    inputSchema = JsonSerializer.Deserialize<JsonElement>("""
                        {"type":"object","properties":{"query":{"type":"string","x-mcp-header":"Query"}},"required":["query"]}
                        """) } } },
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
        var factory = NewFactory(secrets.Object, new ToolResultModelProjector());
        await using (var session = await factory.OpenAsync(server, Token, entered ? "entered-token" : null))
        {
            var tool = session.Tools.ShouldHaveSingleItem();
            tool.OriginalName.ShouldBe(originalName);
            tool.ModelDefinition.Name.Length.ShouldBeLessThanOrEqualTo(64);
            tool.ModelDefinition.Name.ShouldNotContain('.');
            tool.ModelDefinition.InputSchema.GetProperty("properties").GetProperty("query")
                .GetProperty("x-mcp-header").GetString().ShouldBe("Query");
            Should.Throw<ArgumentException>(() => session.ValidateArguments(tool, "{}"));
            var result = await session.CallAsync(tool, "{\"query\":\"test\"}", null, Token);
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
        var factory = NewFactory(Mock.Of<IGlobalSecretStore>(), new ToolResultModelProjector());
        var error = await Should.ThrowAsync<InvalidOperationException>(() => factory.OpenAsync(server, Token));
        error.Message.ShouldContain("not started");
    }

    private static McpServerSettings Server(string transport, string? command = null, string? directory = null) =>
        new(Guid.NewGuid(), "External", transport, true, "Ask", null, command, [], directory, [], false);

    /// <summary>Supplies the path algebra and file-system contract the factory gained.</summary>
    [Fact]
    public async Task TheNextTurnShouldReuseTheRunningServerInsteadOfStartingAnother()
    {
        var server = Server("Stdio", BuiltInCommand, BuiltInDirectory);
        var repository = Settings(server);
        var environment = new CountingEnvironment();
        var connector = NewConnector(Mock.Of<IGlobalSecretStore>(), environment);
        await using var pool = NewPool(connector);
        var lists = new ToolLists();
        var factory = NewComposite(repository, connector, pool, lists);

        await RunScriptAsync(factory, server.Id);
        // The first session listed the tools, so the second offers them without starting anything
        // and reaches the same process when called.
        lists.Stored.ShouldContainKey(server.Id);
        await RunScriptAsync(factory, server.Id);

        environment.Launches.ShouldBe(1);
    }

    [Fact]
    public async Task AServerWithAKnownToolListShouldStartOnlyWhenCalledAndFailOnlyThatCall()
    {
        var server = Server("Stdio", "missing-mcp-server-" + Guid.NewGuid().ToString("N"), BuiltInDirectory);
        var repository = Settings(server);
        var secrets = Mock.Of<IGlobalSecretStore>();
        await using var pool = NewPool(secrets);
        var lists = new ToolLists();
        var tool = JsonSerializer.Deserialize<JsonElement>("""
            {"name":"inspect","description":"Inspect","inputSchema":{"type":"object","properties":{}}}
            """);
        await lists.SetAsync(server.Id, await NewConnector(secrets).FingerprintAsync(server, Token), [tool], Token);
        var factory = NewComposite(repository, secrets, pool, lists);

        await using var session = await factory.OpenAsync([], new HashSet<Guid> { server.Id }, ToolRunContext.None, Token);
        session.Failures.ShouldBeEmpty();
        var offered = session.Tools.ShouldHaveSingleItem();
        offered.OriginalName.ShouldBe("inspect");

        var result = await session.CallAsync(offered, "{}", null, Token);
        result.IsError.ShouldBeTrue();
        result.ModelContent.ShouldContain("could not be started");
    }

    [Fact]
    public async Task AServerThatCannotStartShouldBeLeftOutWhileTheOthersServe()
    {
        var working = Server("Stdio", BuiltInCommand, BuiltInDirectory);
        var broken = Server("Stdio", "missing-mcp-server-" + Guid.NewGuid().ToString("N"), BuiltInDirectory) with { Name = "Broken" };
        var repository = Settings(working, broken);
        await using var pool = NewPool(Mock.Of<IGlobalSecretStore>());
        var factory = NewComposite(repository, Mock.Of<IGlobalSecretStore>(), pool, new ToolLists());

        await using var session = await factory.OpenAsync([], new HashSet<Guid> { working.Id, broken.Id }, ToolRunContext.None, Token);

        session.Tools.ShouldContain(tool => tool.ServerId == working.Id);
        session.Tools.ShouldNotContain(tool => tool.ServerId == broken.Id);
        var failure = session.Failures.ShouldHaveSingleItem();
        failure.ServerId.ShouldBe(broken.Id);
        failure.ServerName.ShouldBe("Broken");
    }

    private static async Task RunScriptAsync(CompositeToolSessionFactory factory, Guid serverId)
    {
        await using var session = await factory.OpenAsync([], new HashSet<Guid> { serverId }, ToolRunContext.None, Token);
        var script = session.Tools.Single(tool => tool.OriginalName == "cs_run");
        var result = await session.CallAsync(script, "{\"code\":\"40 + 2\"}", null, Token);
        result.IsError.ShouldBeFalse();
        result.StructuredContent!.Value.GetProperty("returnValue").GetString().ShouldBe("42");
    }

    private static IGlobalSettingsRepository Settings(params McpServerSettings[] servers)
    {
        var repository = new Mock<IGlobalSettingsRepository>();
        repository.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSettings([], servers, []));
        return repository.Object;
    }

    private static ExternalMcpConnector NewConnector(IGlobalSecretStore secrets, IToolEnvironment? environment = null) =>
        new(secrets, new SystemFileSystem(), new SystemPath(), environment ?? new LocalToolEnvironment());

    private static ExternalToolSessionFactory NewFactory(IGlobalSecretStore secrets,
        IToolResultModelProjector projector) => new(NewConnector(secrets), projector);

    private static ExternalMcpServerPool NewPool(IGlobalSecretStore secrets) => NewPool(NewConnector(secrets));

    private static ExternalMcpServerPool NewPool(IExternalMcpConnector connector)
    {
        var users = new Mock<AI.Application.Users.IUserContext>();
        users.Setup(item => item.Current).Returns(AI.Domain.Users.UserId.Local);
        return new ExternalMcpServerPool(connector, users.Object);
    }

    private static CompositeToolSessionFactory NewComposite(IGlobalSettingsRepository settings, IGlobalSecretStore secrets,
        ExternalMcpServerPool pool, IMcpToolListCache lists) => NewComposite(settings, NewConnector(secrets), pool, lists);

    private static CompositeToolSessionFactory NewComposite(IGlobalSettingsRepository settings, IExternalMcpConnector connector,
        ExternalMcpServerPool pool, IMcpToolListCache lists) =>
        new([], settings, connector, pool, lists, Mock.Of<AI.Application.Resources.IResourceAssetService>(),
            new ToolResultModelProjector(), Microsoft.Extensions.Logging.Abstractions.NullLogger<CompositeToolSessionFactory>.Instance);

    /// <summary>Starts processes as the Host does and counts them.</summary>
    private sealed class CountingEnvironment : IToolEnvironment
    {
        private readonly LocalToolEnvironment _local = new();
        private int _launches;

        public int Launches => _launches;

        public ModelContextProtocol.Client.IClientTransport Launch(ToolProcessLaunch launch)
        {
            Interlocked.Increment(ref _launches);
            return _local.Launch(launch);
        }
    }

    /// <summary>The tool-list cache, kept in memory.</summary>
    private sealed class ToolLists : IMcpToolListCache
    {
        public Dictionary<Guid, (string Fingerprint, IReadOnlyList<JsonElement> Tools)> Stored { get; } = [];

        public Task<IReadOnlyList<JsonElement>?> GetAsync(Guid serverId, string fingerprint, CancellationToken cancellationToken) =>
            Task.FromResult(Stored.TryGetValue(serverId, out var entry) && entry.Fingerprint == fingerprint ? entry.Tools : null);

        public Task SetAsync(Guid serverId, string fingerprint, IReadOnlyList<JsonElement> tools, CancellationToken cancellationToken)
        {
            Stored[serverId] = (fingerprint, tools);
            return Task.CompletedTask;
        }
    }
}


namespace AI.Application.Tests.Settings;

using AI.Application.Settings;
using AI.Contracts.Settings;
using Moq;
using Shouldly;
using Xunit;

public class GlobalSettingsItemTests
{
    [Fact]
    public async Task UpsertingOneMcpServerKeepsOtherDefinitionsAndSecrets()
    {
        var first = Server("First", "old");
        var other = Server("Other", "other");
        var connection = Connection("Connection", true);
        var policy = new McpToolPolicySettings(other.Id, "read", "hash", "Deny", 1, 10);
        var repository = new MemoryRepository(new GlobalSettings([connection], [first, other], [policy]));
        var secrets = new MemorySecrets();
        await secrets.SetAsync("mcp-env", first.Id, "{\"TOKEN\":\"saved\"}", CancellationToken.None);
        await secrets.SetAsync("mcp-env", other.Id, "{\"TOKEN\":\"unrelated\"}", CancellationToken.None);
        secrets.Writes.Clear();

        var edited = first with { Name = "First edited", Url = "https://new.example/mcp" };
        await Service(repository, secrets).UpsertMcpServerAsync(edited, first, CancellationToken.None);

        repository.State.McpServers.Single(item => item.Id == first.Id).Name.ShouldBe("First edited");
        repository.State.McpServers.Single(item => item.Id == other.Id).ShouldBe(other);
        repository.State.Connections.ShouldHaveSingleItem().ShouldBe(connection);
        repository.State.ToolPolicies.ShouldHaveSingleItem().ShouldBe(policy);
        (await secrets.GetAsync("mcp-env", other.Id, CancellationToken.None)).ShouldBe("{\"TOKEN\":\"unrelated\"}");
        secrets.Writes.ShouldBe([( "mcp-env", first.Id )]);
    }

    [Fact]
    public async Task StaleMcpDefinitionCannotOverwriteAnotherEdit()
    {
        var original = Server("Original", "old");
        var repository = new MemoryRepository(new GlobalSettings([], [original with { Name = "Changed elsewhere" }], []));
        var secrets = new MemorySecrets();

        await Should.ThrowAsync<SettingsConflictException>(() => Service(repository, secrets)
            .UpsertMcpServerAsync(original with { Name = "My edit" }, original, CancellationToken.None));

        repository.Writes.ShouldBe(0);
        secrets.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpsertingConnectionChangesOnlyTheRequiredDefaultFlag()
    {
        var first = Connection("First", true);
        var second = Connection("Second", false);
        var server = Server("MCP", "one");
        var repository = new MemoryRepository(new GlobalSettings([first, second], [server], []));

        await Service(repository, new MemorySecrets()).UpsertConnectionAsync(
            second with { IsDefault = true, GoodFor = "coding" }, second, CancellationToken.None);

        repository.State.Connections.Single(item => item.Id == first.Id).ShouldBe(first with { IsDefault = false });
        repository.State.Connections.Single(item => item.Id == second.Id).GoodFor.ShouldBe("coding");
        repository.State.McpServers.ShouldHaveSingleItem().ShouldBe(server);
    }

    [Fact]
    public async Task RemovingMcpServerRemovesOnlyItsGlobalPolicies()
    {
        var first = Server("First", "one");
        var other = Server("Other", "two");
        var firstPolicy = new McpToolPolicySettings(first.Id, "read", "one", "Ask", 1, 10);
        var otherPolicy = new McpToolPolicySettings(other.Id, "read", "two", "Deny", 1, 10);
        var repository = new MemoryRepository(new GlobalSettings([], [first, other], [firstPolicy, otherPolicy]));

        await Service(repository, new MemorySecrets()).RemoveMcpServerAsync(first.Id, first, CancellationToken.None);

        repository.State.McpServers.ShouldHaveSingleItem().ShouldBe(other);
        repository.State.ToolPolicies.ShouldHaveSingleItem().ShouldBe(otherPolicy);
    }

    private static GlobalSettingsService Service(MemoryRepository repository, MemorySecrets secrets) => new(
        repository, secrets, new ConnectionContextLimitsResolver(), Mock.Of<IConnectionModelsResolver>());

    private static McpServerSettings Server(string name, string suffix) => new(
        Guid.CreateVersion7(), name, "StreamableHttp", true, "Ask", $"https://{suffix}.example/mcp",
        null, [], null, [new McpEnvironmentVariableSettings("TOKEN", null, true, true)], false);

    private static ConnectionSettings Connection(string name, bool isDefault) => new(
        Guid.CreateVersion7(), name, "https://example.com/v1", "model", true, isDefault, false);

    private sealed class MemoryRepository(GlobalSettings state) : IGlobalSettingsRepository
    {
        public GlobalSettings State { get; private set; } = state;
        public int Writes { get; private set; }

        public Task<GlobalSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(State);

        public Task SaveAsync(GlobalSettings settings, CancellationToken cancellationToken)
        {
            State = settings;
            Writes++;
            return Task.CompletedTask;
        }

        public async Task<GlobalSettings> UpdateAsync(
            Func<GlobalSettings, CancellationToken, Task<GlobalSettings>> update, CancellationToken cancellationToken)
        {
            State = await update(State, cancellationToken);
            Writes++;
            return State;
        }
    }

    private sealed class MemorySecrets : IGlobalSecretStore
    {
        private readonly Dictionary<(string Scope, Guid Id), string?> _values = [];
        public List<(string Scope, Guid Id)> Writes { get; } = [];

        public Task<string?> GetAsync(string scope, Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_values.GetValueOrDefault((scope, id)));

        public Task SetAsync(string scope, Guid id, string? value, CancellationToken cancellationToken)
        {
            _values[(scope, id)] = value;
            Writes.Add((scope, id));
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string scope, Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_values.GetValueOrDefault((scope, id)) is not null);
    }
}

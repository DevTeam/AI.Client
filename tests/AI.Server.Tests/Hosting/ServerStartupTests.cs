namespace AI.Server.Tests.Hosting;

using AI.Application.Projects;
using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Contracts.Settings;
using AI.Domain.Chats;
using AI.Domain.Projects;
using AI.Domain.Runs;
using AI.Infrastructure.Storage;
using AI.Server.Hosting;
using Moq;
using Shouldly;
using System.Net.Http.Json;
using Xunit;

[Trait("Category", "Integration")]
public sealed class ServerStartupTests
{
    [Fact]
    public async Task SubmitsAndReplacesABranchWithTheProductionComposition()
    {
        var token = TestContext.Current.CancellationToken;
        var directory = Path.Combine(AppContext.BaseDirectory, "startup-submit-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var composition = new StartupComposition(new ServerOptions(directory, "http://127.0.0.1:0", false,
                StopOnProcessSignals: false));
            await using var server = await composition.Server.StartAsync(composition, token);
            using var http = new HttpClient { BaseAddress = server.Address };
            using var projectResponse = await http.PostAsJsonAsync("/api/projects",
                new AI.Contracts.Projects.CreateProjectRequest("Project", ""), token);
            projectResponse.EnsureSuccessStatusCode();
            var project = await projectResponse.Content.ReadFromJsonAsync<AI.Contracts.Projects.ProjectDetails>(token);
            project.ShouldNotBeNull();
            using var chatResponse = await http.PostAsJsonAsync($"/api/projects/{project.Id}/chats",
                new CreateChatRequest("Chat"), token);
            chatResponse.EnsureSuccessStatusCode();
            var chat = await chatResponse.Content.ReadFromJsonAsync<ChatDetails>(token);
            chat.ShouldNotBeNull();
            var sourceId = Guid.NewGuid();
            using var appendResponse = await http.PostAsJsonAsync($"/api/projects/{project.Id}/chats/{chat.Id}/messages",
                new AppendChatMessageRequest(sourceId, null, "User", "First", chat.Revision), token);
            appendResponse.EnsureSuccessStatusCode();
            using var submitResponse = await http.PostAsJsonAsync($"/api/projects/{project.Id}/chats/{chat.Id}/submit",
                new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Queued", ChatSubmitMode.Queue, chat.Id), token);
            submitResponse.EnsureSuccessStatusCode();
            using var replaceResponse = await http.PostAsJsonAsync($"/api/projects/{project.Id}/chats/{chat.Id}/submit",
                new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Replacement", ChatSubmitMode.Replace,
                    chat.Id, ReplaceSourceId: sourceId), token);
            replaceResponse.EnsureSuccessStatusCode();
            ChatRunSnapshot? run = null;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var runs = await http.GetFromJsonAsync<ChatRunSnapshot[]>("/api/runs", token);
                run = runs?.SingleOrDefault(item => item.ChatId == chat.Id);
                if (run?.Status == ChatRunStatus.Failed) break;
                await Task.Delay(50, token);
            }
            run.ShouldNotBeNull().Status.ShouldBe(ChatRunStatus.Failed);
            run.Error.ShouldBe("Choose an enabled connection for this chat.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RestartsWithASavedChatRun()
    {
        var token = TestContext.Current.CancellationToken;
        var directory = Path.Combine(AppContext.BaseDirectory, "startup-run-" + Guid.NewGuid().ToString("N"));
        var options = new ServerOptions(directory, "http://127.0.0.1:0", false, StopOnProcessSignals: false);
        try
        {
            var projectId = Guid.NewGuid();
            var chatId = Guid.NewGuid();
            var location = new Mock<IProjectStorageLocation>();
            location.SetupGet(item => item.RootDirectory).Returns(directory);
            var files = new PhysicalTextFileSystem();
            using (var chats = new JsonChatRepository(files, new ChatStoragePaths(location.Object), new ChatDocumentSerializer()))
            {
                var chat = new ChatThread(new ChatId(chatId), new ProjectId(projectId), "Chat", DateTimeOffset.UnixEpoch);
                (await chats.SaveAsync(chat, 0, token)).IsSaved.ShouldBeTrue();
            }
            using (var runs = new JsonChatRunRepository(files, new ChatRunStoragePaths(location.Object)))
                await runs.SaveAsync(new ChatRunState(projectId, chatId, chatId), token);

            using var restarted = new StartupComposition(options);
            await using var server = await restarted.Server.StartAsync(restarted, token);
            using var http = new HttpClient { BaseAddress = server.Address };
            using var response = await http.GetAsync("/api/health", token);
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task UpdatePreferencesAreHostOwnedAndForeignOriginsCannotInstall()
    {
        var token = TestContext.Current.CancellationToken;
        var directory = Path.Combine(AppContext.BaseDirectory, "startup-updates-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var composition = new StartupComposition(new ServerOptions(directory, "http://127.0.0.1:0", false, StopOnProcessSignals: false));
            await using var server = await composition.Server.StartAsync(composition, token);
            using var http = new HttpClient { BaseAddress = server.Address };
            var state = await http.GetFromJsonAsync<AI.Contracts.Updates.UpdateState>("api/updates", token);
            state.ShouldNotBeNull().Product.ShouldBe("Host");
            state.Preferences.InstallAutomatically.ShouldBeTrue();
            var preferences = new AI.Contracts.Updates.UpdatePreferences(false, false, false, AI.Contracts.Updates.UpdateChannel.Stable);
            using var save = await http.PutAsJsonAsync("api/updates/preferences", preferences, token);
            save.EnsureSuccessStatusCode();
            state = await http.GetFromJsonAsync<AI.Contracts.Updates.UpdateState>("api/updates", token);
            state.ShouldNotBeNull().Preferences.ShouldBe(preferences);
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/updates/install");
            request.Headers.Add("Origin", "https://untrusted.invalid");
            using var denied = await http.SendAsync(request, token);
            denied.StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    [Fact]
    public async Task ProductionServiceProviderBuildsRoutesAndResolvesExternalMcpDiscovery()
    {
        var token = TestContext.Current.CancellationToken;
        var directory = Path.Combine(AppContext.BaseDirectory, "startup-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var composition = new StartupComposition(new ServerOptions(directory, "http://127.0.0.1:0", false,
                StopOnProcessSignals: false));
            await using var server = await composition.Server.StartAsync(composition, token);
            using var http = new HttpClient { BaseAddress = server.Address };
            using var health = await http.GetAsync("/api/health", token);
            health.EnsureSuccessStatusCode();
            var command = "AI.Mcp.CSharp" + (OperatingSystem.IsWindows() ? ".exe" : "");
            var settings = new McpServerSettings(Guid.NewGuid(), "CSharp", "Stdio", true, "Ask", null,
                command, [], Path.Combine(AppContext.BaseDirectory, "mcp-csharp"), [], false);
            using var response = await http.PostAsJsonAsync("/api/mcp/tools/discover",
                new DiscoverMcpToolsRequest(settings), token);
            response.EnsureSuccessStatusCode();
            var tools = await response.Content.ReadFromJsonAsync<McpToolInfo[]>(token);
            tools.ShouldNotBeNull().ShouldHaveSingleItem().Name.ShouldBe("cs_run");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}

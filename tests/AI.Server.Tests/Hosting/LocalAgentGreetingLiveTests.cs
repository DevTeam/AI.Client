namespace AI.Server.Tests.Hosting;

using System.Net.Http.Json;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Runs;
using AI.Contracts.Settings;
using AI.Server.Hosting;
using Xunit;

public sealed class LocalAgentGreetingLiveTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("AI_LOCAL_AGENT_LIVE_TEST") == "1";

    [Fact(Skip = "Set AI_LOCAL_AGENT_LIVE_TEST=1 and AI_LOCAL_MODEL_DIRECTORY to an isolated 4K package.", SkipUnless = nameof(Enabled))]
    public async Task GreetingCompletesThroughTheProductionAgentWithFourThousandTokens()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        var directory = Path.Combine(Path.GetTempPath(), "ai-agent-greeting-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var composition = new StartupComposition(new ServerOptions(directory, "http://127.0.0.1:0", false,
                StopOnProcessSignals: false));
            await using var server = await composition.Server.StartAsync(composition, token);
            using var http = new HttpClient { BaseAddress = server.Address };
            while ((await http.GetFromJsonAsync<LocalModelStatus>("api/settings/local-ai", token))?.State != "ready")
                await Task.Delay(100, token);
            var settings = await http.GetFromJsonAsync<GlobalSettings>("api/settings", token);
            var local = Assert.Single(settings!.Connections, item => item.Id == LocalModelStatus.ConnectionId);
            Assert.Equal(4096, local.ContextWindowTokens);
            using var projectResponse = await http.PostAsJsonAsync("api/projects", new CreateProjectRequest("Greeting", ""), token);
            projectResponse.EnsureSuccessStatusCode();
            var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDetails>(token))!;
            using var chatResponse = await http.PostAsJsonAsync($"api/projects/{project.Id}/chats", new CreateChatRequest("Greeting"), token);
            chatResponse.EnsureSuccessStatusCode();
            var chat = (await chatResponse.Content.ReadFromJsonAsync<ChatDetails>(token))!;
            using var submitted = await http.PostAsJsonAsync($"api/projects/{project.Id}/chats/{chat.Id}/submit",
                new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Привет!", ChatSubmitMode.Send, chat.Id), token);
            submitted.EnsureSuccessStatusCode();
            ChatRunSnapshot? run;
            do
            {
                await Task.Delay(100, token);
                run = (await http.GetFromJsonAsync<ChatRunSnapshot[]>("api/runs", token))?.SingleOrDefault(item => item.ChatId == chat.Id);
            } while (run is null || run.Status is ChatRunStatus.Generating or ChatRunStatus.Idle);
            Assert.True(run.Status == ChatRunStatus.Completed, run.Error ?? run.Status.ToString());
        }
        finally
        {
            if (Environment.GetEnvironmentVariable("AI_LOCAL_AGENT_REPORT_DIRECTORY") is { Length: > 0 } reports
                && Directory.Exists(directory))
            {
                Directory.CreateDirectory(reports);
                foreach (var log in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Where(path => Path.GetExtension(path) is ".log" or ".jsonl" && path.Contains("logs", StringComparison.Ordinal)))
                    File.Copy(log, Path.Combine(reports, Path.GetFileName(log)), overwrite: true);
            }
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}

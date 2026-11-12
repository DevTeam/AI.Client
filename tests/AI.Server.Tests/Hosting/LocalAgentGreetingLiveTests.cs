namespace AI.Server.Tests.Hosting;

using System.Net.Http.Json;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Runs;
using AI.Contracts.Settings;
using AI.Server.Hosting;
using Xunit;

/// <summary>
/// The real local model through the production Host and agent. "greeting" answers "Привет!";
/// "directory" then asks which files the granted workspace holds; "edit" asks to change a word in
/// two files there and checks the files.
/// </summary>
public sealed class LocalAgentGreetingLiveTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("AI_LOCAL_AGENT_LIVE_TEST") == "1";

    [Theory(Skip = "Set AI_LOCAL_AGENT_LIVE_TEST=1 and AI_LOCAL_MODEL_DIRECTORY to an isolated local model package.", SkipUnless = nameof(Enabled))]
    [InlineData("greeting")]
    [InlineData("directory")]
    [InlineData("edit")]
    public async Task GreetingCompletesThroughTheProductionAgentWithThePackageContext(string scenario)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(
            int.TryParse(Environment.GetEnvironmentVariable("AI_LOCAL_AGENT_TIMEOUT_SECONDS"), out var seconds) ? seconds : 180));
        var token = timeout.Token;
        var english = Environment.GetEnvironmentVariable("AI_LOCAL_AGENT_ENGLISH_CONTROL") == "1";
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
            var manifest = System.Text.Json.JsonSerializer.Deserialize<LocalModelManifest>(await File.ReadAllTextAsync(
                Path.Combine(Environment.GetEnvironmentVariable("AI_LOCAL_MODEL_DIRECTORY")!, "manifest.json"), token))!;
            Assert.Equal(manifest.ContextTokens, local.ContextWindowTokens);
            var workspace = Path.Combine(directory, "workspace");
            Directory.CreateDirectory(workspace);
            var alpha = Path.Combine(workspace, "alpha.txt");
            var notes = Path.Combine(workspace, "notes.md");
            await File.WriteAllTextAsync(alpha, scenario == "edit" ? "The colour is red.\n" : "alpha", token);
            await File.WriteAllTextAsync(notes, scenario == "edit" ? "# Notes\nPaint the door red.\n" : "notes", token);
            var usesWorkspace = scenario != "greeting";
            using var projectResponse = await http.PostAsJsonAsync("api/projects", new CreateProjectRequest("Greeting",
                usesWorkspace ? $"The user's working directory is {workspace}." : ""), token);
            projectResponse.EnsureSuccessStatusCode();
            var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDetails>(token))!;
            if (usesWorkspace)
            {
                using var security = await http.PutAsJsonAsync($"api/projects/{project.Id}/security",
                    new UpdateProjectSecurityRequest(project.Revision,
                        [new DirectoryGrantSettings(Guid.NewGuid(), "Workspace", workspace, true,
                            scenario == "edit" ? ["read", "write", "edit"] : ["read"])],
                        project.McpServers, project.ToolPolicies), token);
                security.EnsureSuccessStatusCode();
            }
            using var chatResponse = await http.PostAsJsonAsync($"api/projects/{project.Id}/chats", new CreateChatRequest("Greeting"), token);
            chatResponse.EnsureSuccessStatusCode();
            var chat = (await chatResponse.Content.ReadFromJsonAsync<ChatDetails>(token))!;
            if (scenario == "edit")
            {
                // No one is there to confirm the writes.
                using var approval = await http.PutAsJsonAsync($"api/projects/{project.Id}/chats/{chat.Id}/approval-mode",
                    new UpdateChatApprovalModeRequest(ToolApprovalMode.FullAccess), token);
                approval.EnsureSuccessStatusCode();
            }
            try
            {
                async Task SendAsync(string text)
                {
                    using var response = await http.PostAsJsonAsync($"api/projects/{project.Id}/chats/{chat.Id}/submit",
                        new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), text, ChatSubmitMode.Send, chat.Id), token);
                    response.EnsureSuccessStatusCode();
                    ChatRunSnapshot? run;
                    do
                    {
                        await Task.Delay(100, token);
                        run = (await http.GetFromJsonAsync<ChatRunSnapshot[]>("api/runs", token))?.SingleOrDefault(item => item.ChatId == chat.Id);
                    } while (run is null || run.Status is ChatRunStatus.Generating or ChatRunStatus.Idle);
                    Assert.True(run.Status == ChatRunStatus.Completed, run.Error ?? run.Status.ToString());
                }

                await SendAsync("Привет!");
                if (scenario == "directory")
                {
                    foreach (var prompt in english
                                 ? new[] { "What files do I have?", "In my working directory." }
                                 : ["какие файлы у менят есть", "в мой директории"])
                        await SendAsync(prompt);
                    var transcript = (await http.GetFromJsonAsync<ChatDetails>($"api/projects/{project.Id}/chats/{chat.Id}", token))!;
                    Assert.Contains(transcript.Messages, message => message.ToolCalls?.Any(call => call.Name.Contains("list_directory", StringComparison.Ordinal)) == true);
                    Assert.Contains(transcript.Messages, message => message.Role == "Tool" && message.ToolResultIsError != true
                        && message.Content.Contains("alpha.txt", StringComparison.Ordinal));
                    var answer = transcript.Messages.Last(message => message.Role == "Assistant" && message.ToolCalls is not { Count: > 0 }).Content;
                    Assert.Contains("alpha.txt", answer);
                    Assert.Contains("notes.md", answer);
                }
                if (scenario == "edit")
                {
                    await SendAsync(english
                        ? "In both files of my working directory, alpha.txt and notes.md, replace the word red with blue."
                        : "В обоих файлах моей рабочей папки, alpha.txt и notes.md, замени слово red на blue.");
                    Assert.Equal("The colour is blue.\n", (await File.ReadAllTextAsync(alpha, token)).ReplaceLineEndings("\n"));
                    Assert.Equal("# Notes\nPaint the door blue.\n", (await File.ReadAllTextAsync(notes, token)).ReplaceLineEndings("\n"));
                }
            }
            finally
            {
                // Saved on failure too, while the server is still running, so a red run can be diagnosed.
                if (Environment.GetEnvironmentVariable("AI_LOCAL_AGENT_REPORT_DIRECTORY") is { Length: > 0 } report)
                {
                    Directory.CreateDirectory(report);
                    var snapshot = await http.GetStringAsync($"api/projects/{project.Id}/chats/{chat.Id}", CancellationToken.None);
                    await File.WriteAllTextAsync(Path.Combine(report, scenario + "-transcript.json"), snapshot, CancellationToken.None);
                }
            }
        }
        finally
        {
            if (Environment.GetEnvironmentVariable("AI_LOCAL_AGENT_REPORT_DIRECTORY") is { Length: > 0 } reports
                && Directory.Exists(directory))
            {
                Directory.CreateDirectory(reports);
                foreach (var log in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Where(path => Path.GetExtension(path) is ".log" or ".jsonl" && path.Contains("logs", StringComparison.Ordinal)))
                    File.Copy(log, Path.Combine(reports, scenario + "-" + Path.GetFileName(log)), overwrite: true);
            }
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}

namespace AI.Agent.Evals;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Runs;
using AI.Contracts.Settings;
using AI.Contracts.Usage;

/// <summary>
/// A Host of its own for one attempt: its own data directory and port, so nothing — settings,
/// memory, usage, tool policies — leaks between attempts or from the person's own installation.
/// </summary>
public sealed class EvalHost : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly Process _process;
    private readonly HttpClient _http;

    private EvalHost(Process process, HttpClient http)
    {
        _process = process;
        _http = http;
    }

    public static async Task<EvalHost> StartAsync(string hostAssembly, string dataDirectory, string logPath, CancellationToken token)
    {
        var port = FreePort();
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(hostAssembly)!
        };
        foreach (var argument in new[] { hostAssembly, "--data-dir", dataDirectory, "--urls", $"http://127.0.0.1:{port}", "--no-browse" })
            start.ArgumentList.Add(argument);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment.Remove("AI_CLIENT_DATA_DIRECTORY");

        var process = Process.Start(start) ?? throw new InvalidOperationException("The Host did not start.");
        var log = new StreamWriter(logPath) { AutoFlush = true };
        process.OutputDataReceived += (_, line) => { if (line.Data is not null) lock (log) log.WriteLine(line.Data); };
        process.ErrorDataReceived += (_, line) => { if (line.Data is not null) lock (log) log.WriteLine(line.Data); };
        process.Exited += (_, _) => { lock (log) log.Dispose(); };
        process.EnableRaisingEvents = true;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromMinutes(2) };
        var host = new EvalHost(process, http);
        try
        {
            var deadline = Stopwatch.StartNew();
            while (true)
            {
                if (process.HasExited) throw new InvalidOperationException($"The Host exited with code {process.ExitCode}; see {logPath}.");
                try
                {
                    using var health = await http.GetAsync(new Uri("api/health", UriKind.Relative), token);
                    if (health.IsSuccessStatusCode) return host;
                }
                catch (HttpRequestException) { }

                if (deadline.Elapsed > TimeSpan.FromSeconds(90)) throw new TimeoutException($"The Host did not answer in 90 s; see {logPath}.");
                await Task.Delay(250, token);
            }
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    /// <summary>The evaluated connection as the only and default one, and only the automation the scenario measures.</summary>
    public async Task<Guid> ConfigureAsync(EvalModel model, CancellationToken token)
    {
        var connection = new ConnectionSettings(Guid.CreateVersion7(), "Evaluation", model.BaseUrl, model.Model, true, true, false,
            ContextWindowTokens: model.ContextWindow, Prices: model.Prices, ReasoningEffort: model.ReasoningEffort);
        await SendAsync(HttpMethod.Put, $"api/settings/connections/{connection.Id}",
            new SettingsItemChange<ConnectionSettings>(connection, null), token);
        if (!string.IsNullOrWhiteSpace(model.ApiKey))
            await SendAsync(HttpMethod.Put, $"api/settings/connections/{connection.Id}/credential", new UpdateSecretRequest(model.ApiKey), token);
        // Titles and reply suggestions cost requests a person running a task never sees, and their
        // tokens would blur the numbers; routing is part of answering, so it stays as configured.
        await SendAsync(HttpMethod.Put, "api/settings/chat-automation",
            new ChatAutomationSettings(AutoTitle: false, SuggestReplies: false, RouteSkills: model.RouteSkills, SuggestComments: false), token);
        return connection.Id;
    }

    public async Task<ProjectDetails> CreateProjectAsync(string name, string? workspace, CancellationToken token)
    {
        var project = await SendAsync<ProjectDetails>(HttpMethod.Post, "api/projects", new CreateProjectRequest(name, "Agent evaluation"), token);
        if (workspace is null) return project;
        var grant = new DirectoryGrantSettings(Guid.CreateVersion7(), "workspace", workspace, true, ["read", "write", "edit", "delete"]);
        return await SendAsync<ProjectDetails>(HttpMethod.Put, $"api/projects/{project.Id}/security",
            new UpdateProjectSecurityRequest(project.Revision, [grant], project.McpServers, project.ToolPolicies), token);
    }

    // Approvals are answered by the runner, so FullAccess only spares it a card per safe call;
    // the policies of the tools still apply.
    public Task<ChatDetails> CreateChatAsync(Guid projectId, Guid connectionId, string title, CancellationToken token) =>
        SendAsync<ChatDetails>(HttpMethod.Post, $"api/projects/{projectId}/chats",
            new CreateChatRequest(title, connectionId, ApprovalMode: ToolApprovalMode.FullAccess), token);

    public Task<ChatRunSnapshot> SubmitAsync(Guid projectId, Guid chatId, string content, CancellationToken token) =>
        SendAsync<ChatRunSnapshot>(HttpMethod.Post, $"api/projects/{projectId}/chats/{chatId}/submit",
            new SubmitChatMessageRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), content, BranchId: chatId), token);

    public async Task<ChatRunSnapshot?> GetRunAsync(Guid chatId, CancellationToken token)
    {
        var runs = await _http.GetFromJsonAsync<ChatRunSnapshot[]>(new Uri("api/runs", UriKind.Relative), Web, token) ?? [];
        return runs.FirstOrDefault(run => run.ChatId == chatId && run.BranchId == chatId);
    }

    public Task<bool> DecideAsync(ChatRunSnapshot run, ToolApprovalDecision decision, CancellationToken token) =>
        TrySendAsync($"api/projects/{run.ProjectId}/chats/{run.ChatId}/tools/decision?branchId={run.BranchId}", decision, token);

    public Task<bool> AnswerAsync(ChatRunSnapshot run, UserPromptResponse response, CancellationToken token) =>
        TrySendAsync($"api/projects/{run.ProjectId}/chats/{run.ChatId}/prompts/answer?branchId={run.BranchId}", response, token);

    public Task<bool> StopAsync(Guid projectId, Guid chatId, CancellationToken token) =>
        TrySendAsync($"api/projects/{projectId}/chats/{chatId}/stop?branchId={chatId}&operationId={Guid.CreateVersion7()}", new { }, token);

    public async Task<JsonElement> GetChatJsonAsync(Guid projectId, Guid chatId, CancellationToken token) =>
        await _http.GetFromJsonAsync<JsonElement>(new Uri($"api/projects/{projectId}/chats/{chatId}", UriKind.Relative), Web, token);

    public Task<ProjectDetails?> GetProjectAsync(Guid projectId, CancellationToken token) =>
        _http.GetFromJsonAsync<ProjectDetails>(new Uri($"api/projects/{projectId}", UriKind.Relative), Web, token);

    public async Task<ChatTokenUsage?> GetUsageAsync(Guid projectId, Guid chatId, CancellationToken token) =>
        await _http.GetFromJsonAsync<ChatTokenUsage>(new Uri($"api/projects/{projectId}/chats/{chatId}/usage", UriKind.Relative), Web, token);

    public static ChatDetails? ReadChat(JsonElement json) => json.Deserialize<ChatDetails>(Web);

    private async Task<bool> TrySendAsync(string path, object body, CancellationToken token)
    {
        using var response = await _http.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, Web, token);
        return response.IsSuccessStatusCode;
    }

    private async Task SendAsync(HttpMethod method, string path, object body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body, options: Web) };
        using var response = await _http.SendAsync(request, token);
        await EnsureAsync(response, method, path, token);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body, options: Web) };
        using var response = await _http.SendAsync(request, token);
        await EnsureAsync(response, method, path, token);
        return await response.Content.ReadFromJsonAsync<T>(Web, token)
            ?? throw new InvalidOperationException($"{method} {path} returned no body.");
    }

    private static async Task EnsureAsync(HttpResponseMessage response, HttpMethod method, string path, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(token);
        throw new HttpRequestException($"{method} {path} failed with {(int)response.StatusCode}: {body}", null, response.StatusCode);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        if (!_process.HasExited)
        {
            // The tool servers are its children; leaving them would keep files of the workspace open.
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();
    }
}

/// <summary>
/// The endpoint under evaluation, from a JSON file kept out of git (<c>evals/connection.json</c>), each
/// field overridable by an <c>AI_AGENT_EVAL_*</c> variable. The key goes to the Host and nowhere else.
/// </summary>
public sealed record EvalModel(string BaseUrl, string Model, string? ApiKey, long? ContextWindow, TokenPrices? Prices,
    string? ReasoningEffort, bool RouteSkills)
{
    public static EvalModel Load(string path, bool routeSkills)
    {
        var file = File.Exists(path)
            ? JsonSerializer.Deserialize<ConnectionFile>(File.ReadAllText(path), Scenario.Json) ?? new ConnectionFile()
            : new ConnectionFile();
        var url = Variable("BASE_URL") ?? file.BaseUrl;
        var model = Variable("MODEL") ?? file.Model;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
            throw new ArgumentException($"Set an absolute HTTP or HTTPS baseUrl in {path} or AI_AGENT_EVAL_BASE_URL.");
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException($"Set model in {path} or AI_AGENT_EVAL_MODEL.");
        return new EvalModel(url!, model!, Variable("API_KEY") ?? file.ApiKey,
            Variable("CONTEXT_WINDOW") is { } window ? long.Parse(window, System.Globalization.CultureInfo.InvariantCulture) : file.ContextWindow,
            ParsePrices(Variable("PRICES")) ?? file.Prices,
            Variable("REASONING_EFFORT") ?? file.ReasoningEffort,
            routeSkills);
    }

    private static string? Variable(string name) =>
        Environment.GetEnvironmentVariable("AI_AGENT_EVAL_" + name) is { Length: > 0 } value ? value : null;

    private sealed record ConnectionFile(string? BaseUrl = null, string? Model = null, string? ApiKey = null,
        long? ContextWindow = null, string? ReasoningEffort = null, TokenPrices? Prices = null);

    // "input,output[,cachedInput]" per million tokens.
    private static TokenPrices? ParsePrices(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split(',').Select(part => decimal.Parse(part.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return parts.Length switch
        {
            2 => new TokenPrices(parts[0], parts[1]),
            3 => new TokenPrices(parts[0], parts[1], parts[2]),
            _ => throw new ArgumentException("AI_AGENT_EVAL_PRICES is \"input,output[,cachedInput]\" per million tokens.")
        };
    }
}

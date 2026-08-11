using AI.Client.Contracts.Chat;
using System.Diagnostics;
using System.Text.Json;

namespace AI.Client.Cli;

internal interface IHeadlessApplication
{
    Task<int> RunAsync();
}

internal sealed class HeadlessApplication(
    string[] args,
    IHeadlessSessionStore store,
    IHeadlessChatClient chatClient)
    : IHeadlessApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<int> RunAsync()
    {
        try
        {
            return args switch
            {
                ["session", "create", .. var options] => await CreateAsync(Parse(options)),
                ["session", "send", .. var options] => await SendAsync(Parse(options)),
                ["session", "show", .. var options] => await ShowAsync(Parse(options)),
                ["session", "delete", .. var options] => await DeleteAsync(Parse(options)),
                ["agent", ..] => Write(new { status = "not_supported", error = "MCP Agent Runtime is not implemented yet." }, 2),
                _ => Write(new { status = "invalid_arguments", error = Usage }, 2)
            };
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException or ArgumentException)
        {
            return Write(new { status = "failed", error = error.Message }, 1);
        }
    }

    private async Task<int> CreateAsync(Dictionary<string, string> options)
    {
        var host = GetHost(options);
        var projectName = Required(options, "project");
        var projects = await chatClient.GetProjectsAsync(host, CancellationToken.None);
        var summary = projects.SingleOrDefault(item => string.Equals(item.Name, projectName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Project '{projectName}' was not found.");
        var project = await chatClient.GetProjectAsync(host, summary.Id, CancellationToken.None)
            ?? throw new InvalidOperationException($"Project '{projectName}' was not found.");
        var endpoint = SelectEndpoint(project, options.GetValueOrDefault("endpoint"));
        var session = new HeadlessSession(Guid.CreateVersion7(), project.Id, project.Name, endpoint.Id, endpoint.Name,
            endpoint.BaseUrl, endpoint.Model, DateTimeOffset.UtcNow, [], project.McpServers.Length,
            project.ToolPolicies.Length, project.DirectoryGrants.Length);
        await store.SaveAsync(session, CancellationToken.None);
        await store.AppendTranscriptAsync(session.Id, new { type = "session_created", at = DateTimeOffset.UtcNow, session }, CancellationToken.None);
        return Write(new { status = "ready", sessionId = session.Id, session.ProjectName, session.EndpointName,
            security = new { session.McpServerCount, session.ToolPolicyCount, session.DirectoryGrantCount } });
    }

    private async Task<int> SendAsync(Dictionary<string, string> options)
    {
        var sessionId = Guid.Parse(Required(options, "session"));
        var message = Required(options, "message");
        var session = await store.GetAsync(sessionId, CancellationToken.None)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        var host = GetHost(options);
        var turnId = Guid.CreateVersion7();
        var startedAt = Stopwatch.GetTimestamp();
        long firstTokenMs = -1;
        var chunks = new List<string>();
        var context = session.Messages.Append(new ChatCompletionMessage("user", message)).ToArray();
        using var cancellation = new CancellationTokenSource();
        if (options.TryGetValue("cancel-after-ms", out var timeoutValue))
        {
            cancellation.CancelAfter(int.Parse(timeoutValue, System.Globalization.CultureInfo.InvariantCulture));
        }

        try
        {
            await foreach (var chunk in chatClient.StreamAsync(host,
                               new ChatCompletionRequest(session.BaseUrl, session.Model, null, message,
                                   session.EndpointProfileId, context), cancellation.Token))
            {
                if (chunks.Count == 0) firstTokenMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                chunks.Add(chunk.Content);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            var partialText = string.Concat(chunks);
            var cancelled = new HeadlessTurnResult(sessionId, turnId, "cancelled", partialText, chunks.Count,
                firstTokenMs, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, "cancelled", null);
            await store.AppendTranscriptAsync(sessionId,
                new { type = "turn", at = DateTimeOffset.UtcNow, message, result = cancelled }, CancellationToken.None);
            return Write(cancelled);
        }

        var finalText = string.Concat(chunks);
        var messages = session.Messages
            .Append(new ChatCompletionMessage("user", message))
            .Append(new ChatCompletionMessage("assistant", finalText))
            .ToArray();
        session = session with { Messages = messages };
        await store.SaveAsync(session, CancellationToken.None);
        var result = new HeadlessTurnResult(sessionId, turnId, "completed", finalText, chunks.Count,
            firstTokenMs, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, "done", null);
        await store.AppendTranscriptAsync(sessionId, new { type = "turn", at = DateTimeOffset.UtcNow, message, result }, CancellationToken.None);
        return Write(result);
    }

    private async Task<int> ShowAsync(Dictionary<string, string> options)
    {
        var sessionId = Guid.Parse(Required(options, "session"));
        var session = await store.GetAsync(sessionId, CancellationToken.None);
        return session is null ? Write(new { status = "not_found", sessionId }, 1) : Write(new { status = "ready", session });
    }

    private async Task<int> DeleteAsync(Dictionary<string, string> options)
    {
        var sessionId = Guid.Parse(Required(options, "session"));
        await store.DeleteAsync(sessionId, CancellationToken.None);
        return Write(new { status = "deleted", sessionId });
    }

    private static EndpointDto SelectEndpoint(ProjectDetailsDto project, string? endpointName)
    {
        var endpoint = endpointName is null
            ? project.EndpointProfiles.SingleOrDefault(item => item.Id == project.DefaultEndpointProfileId)
            : project.EndpointProfiles.SingleOrDefault(item => string.Equals(item.Name, endpointName, StringComparison.OrdinalIgnoreCase));
        return endpoint ?? throw new InvalidOperationException("The requested endpoint profile was not found and no default endpoint is configured.");
    }

    private static Uri GetHost(IReadOnlyDictionary<string, string> options) =>
        new(options.GetValueOrDefault("host") ?? "http://localhost:52173", UriKind.Absolute);

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required option '--{name}'.");

    private static Dictionary<string, string> Parse(string[] options)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < options.Length; index += 2)
        {
            if (!options[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= options.Length)
                throw new ArgumentException($"Invalid option '{options[index]}'.");
            result[options[index][2..]] = options[index + 1];
        }
        return result;
    }

    private static int Write(object value, int exitCode = 0)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
        return exitCode;
    }

    private const string Usage = "session create --project <name> [--endpoint <name>] [--host <url>] | session send --session <id> --message <text> [--host <url>] [--cancel-after-ms <ms>] | session show --session <id> | session delete --session <id>";
}

namespace AI.Client.Cli;

using Contracts.Chats;
using Contracts.Runs;
using System.Diagnostics;
using System.Text.Json;

internal interface IHeadlessApplication { Task<int> RunAsync(); }

internal sealed class HeadlessApplication(string[] args, IHeadlessSessionStore store, IHeadlessChatClient chatClient) : IHeadlessApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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
        var summary = (await chatClient.GetProjectsAsync(host, CancellationToken.None))
            .SingleOrDefault(project => string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Project not found.");
        var project = await chatClient.GetProjectAsync(host, summary.Id, CancellationToken.None);
        Guid? connectionId = null;
        if (options.TryGetValue("connection", out var name))
        {
            connectionId = (await chatClient.GetSettingsAsync(host, CancellationToken.None)).Connections
                .SingleOrDefault(connection => connection.Enabled && string.Equals(connection.Name, name, StringComparison.OrdinalIgnoreCase))?.Id
                ?? throw new InvalidOperationException("Connection not found.");
        }
        var chat = await chatClient.CreateChatAsync(host, project.Id, new CreateChatRequest("CLI chat", connectionId), CancellationToken.None);
        var session = new HeadlessSession(chat.Id, project.Id, host);
        await store.SaveAsync(session, CancellationToken.None);
        return Write(new { status = "ready", sessionId = session.Id, chatId = chat.Id, project.Name });
    }

    private async Task<int> SendAsync(Dictionary<string, string> options)
    {
        var session = await GetSessionAsync(options);
        var host = options.ContainsKey("host") ? GetHost(options) : session.Host;
        var messageId = Guid.CreateVersion7();
        var started = Stopwatch.GetTimestamp();
        using var timeout = new CancellationTokenSource();
        if (options.TryGetValue("cancel-after-ms", out var value)) timeout.CancelAfter(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
        ChatRunSnapshot? last = null;
        try
        {
            last = await chatClient.SubmitAsync(host, session.ProjectId, session.Id,
                new SubmitChatMessageRequest(Guid.CreateVersion7(), messageId, Required(options, "message")), timeout.Token);
            await foreach (var snapshots in chatClient.WatchAsync(host, timeout.Token))
            {
                last = snapshots.SingleOrDefault(run => run.ChatId == session.Id && run.BranchId == session.Id) ?? last;
                if (last.Status is ChatRunStatus.Failed or ChatRunStatus.Interrupted or ChatRunStatus.Paused)
                    return Write(new { status = last.Status.ToString().ToLowerInvariant(), last.Error, last.StreamingContent }, 1);
                var chat = await chatClient.GetChatAsync(host, session.ProjectId, session.Id, timeout.Token);
                var reply = chat.Messages.FirstOrDefault(item => item.ParentId == messageId && item.Role == "Assistant");
                if (reply is null) continue;
                var result = new { status = "completed", sessionId = session.Id, messageId, finalText = reply.Content,
                    durationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds };
                await store.AppendTranscriptAsync(session.Id, result, CancellationToken.None);
                return Write(result);
            }
            throw new InvalidOperationException("Run event stream ended before completion.");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            await chatClient.StopAsync(host, session.ProjectId, session.Id, CancellationToken.None);
            return Write(new { status = "cancelled", sessionId = session.Id, partialText = last?.StreamingContent });
        }
    }

    private async Task<HeadlessSession> GetSessionAsync(Dictionary<string, string> options) =>
        await store.GetAsync(Guid.Parse(Required(options, "session")), CancellationToken.None)
            ?? throw new InvalidOperationException("Session not found.");

    private async Task<int> ShowAsync(Dictionary<string, string> options)
    {
        var session = await GetSessionAsync(options);
        var chat = await chatClient.GetChatAsync(session.Host, session.ProjectId, session.Id, CancellationToken.None);
        return Write(new { status = "ready", session, chat });
    }

    private async Task<int> DeleteAsync(Dictionary<string, string> options)
    {
        var session = await GetSessionAsync(options);
        var chat = await chatClient.GetChatAsync(session.Host, session.ProjectId, session.Id, CancellationToken.None);
        await chatClient.DeleteChatAsync(session.Host, session.ProjectId, session.Id, chat.Revision, CancellationToken.None);
        await store.DeleteAsync(session.Id, CancellationToken.None);
        return Write(new { status = "deleted", sessionId = session.Id });
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

    private const string Usage = "session create --project <name> [--connection <name>] [--host <url>] | session send --session <id> --message <text> [--host <url>] [--cancel-after-ms <ms>] | session show --session <id> | session delete --session <id>";
}

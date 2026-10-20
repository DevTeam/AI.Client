namespace AI.Agent.Evals;

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Contracts.Usage;

/// <summary>
/// One attempt: a fresh Host and workspace, the scenario's turns sent as a person would send them,
/// approvals and questions answered by the scenario's rules, then the trace and the verdict.
/// </summary>
public sealed class AttemptRunner(string hostAssembly, EvalModel model, WorkspaceSource source)
{
    private static readonly JsonSerializerOptions Indented = new(Scenario.Json) { WriteIndented = true };
    private static readonly JsonSerializerOptions Line = new(Scenario.Json);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(500);

    public async Task<AttemptResult> RunAsync(Scenario scenario, int attempt, string resultDirectory, CancellationToken token)
    {
        Directory.CreateDirectory(resultDirectory);
        var scratch = Path.Combine(Path.GetTempPath(), "ai-agent-evals", Guid.CreateVersion7().ToString("N"));
        var data = Path.Combine(scratch, "data");
        var root = Path.Combine(scratch, "workspace");
        Directory.CreateDirectory(data);
        var before = Workspace.Snapshot(root);

        var trace = new Trace(Path.Combine(resultDirectory, "events.jsonl"));
        var clock = Stopwatch.StartNew();
        var outcome = "error";
        string? error = null;
        int turns = 0, approvals = 0, denied = 0, prompts = 0;
        ChatDetails? chat = null;
        ChatTokenUsage? usage = null;
        try
        {
            await source.PrepareAsync(scenario, root, token);
            before = Workspace.Snapshot(root);
            await using var host = await EvalHost.StartAsync(hostAssembly, data, Path.Combine(resultDirectory, "host.log"), token);
            var connection = await host.ConfigureAsync(model with { ContextWindow = scenario.ContextWindow ?? model.ContextWindow }, token);
            var project = await host.CreateProjectAsync(scenario.Id, root, token);
            var created = await host.CreateChatAsync(project.Id, connection, scenario.Id, token);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(scenario.TimeoutSeconds));
            try
            {
                foreach (var text in scenario.Turns)
                {
                    turns++;
                    var message = text.Replace("{workspace}", root, StringComparison.Ordinal);
                    trace.Write(new { kind = "turn", turn = turns, message });
                    var run = await host.SubmitAsync(project.Id, created.Id, message, deadline.Token);
                    var ended = await DriveAsync(host, scenario, run, trace, deadline.Token);
                    approvals += ended.Approvals;
                    denied += ended.Denied;
                    prompts += ended.Prompts;
                    outcome = ended.Status.ToString().ToLowerInvariant();
                    error = ended.Error;
                    if (ended.Status != ChatRunStatus.Completed) break;
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                outcome = "timeout";
                error = $"No result in {scenario.TimeoutSeconds} s.";
                trace.Write(new { kind = "timeout" });
                await host.StopAsync(project.Id, created.Id, token);
            }

            var json = await host.GetChatJsonAsync(project.Id, created.Id, token);
            await File.WriteAllTextAsync(Path.Combine(resultDirectory, "chat.json"), JsonSerializer.Serialize(json, Indented), token);
            chat = EvalHost.ReadChat(json);
            usage = await host.GetUsageAsync(project.Id, created.Id, token);
            await File.WriteAllTextAsync(Path.Combine(resultDirectory, "usage.json"), JsonSerializer.Serialize(usage, Indented), token);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TimeoutException
            or IOException or JsonException or InvalidDataException)
        {
            outcome = "error";
            error = exception.Message;
            trace.Write(new { kind = "error", error = exception.ToString() });
        }

        clock.Stop();
        var after = Workspace.Snapshot(root);
        var changes = after.ChangesSince(before);
        var messages = chat?.Messages ?? [];
        var calls = messages.SelectMany(message => message.ToolCalls ?? []).ToArray();
        var failedResults = messages.Where(message => message.ToolResultIsError == true).ToArray();
        // A program that ran and exited non-zero — a failing test run, a grep with no match — is a
        // result the model asked for, not a tool that failed; counting them together hides both.
        var nonZeroExits = failedResults.Count(message => ToolResults.IsNonZeroExit(message.Content));
        var toolErrors = failedResults.Length - nonZeroExits;
        var answer = FinalAnswer(messages);
        IReadOnlyList<CheckResult> checks = [];
        if (outcome != "error")
        {
            try
            {
                await source.RevealAsync(scenario, root, token);
                checks = await Checks.RunAsync(scenario.Checks ?? [], before, after, changes,
                    new AttemptFacts(answer, calls.Length, toolErrors, prompts, scenario.Directory,
                        calls.Select(call => $"{call.Name} {call.Arguments}").ToArray()), token);
            }
            catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException)
            {
                outcome = "error";
                error = exception.Message;
            }
        }
        var totals = usage?.Totals;
        var result = new AttemptResult(
            scenario.Id, attempt,
            outcome == "completed" && checks.All(check => check.Passed),
            outcome, error, clock.ElapsedMilliseconds, turns,
            messages.Count(message => Role(message, "Assistant")),
            calls.Length, toolErrors, nonZeroExits,
            calls.Length - calls.Select(call => (call.Name, call.Arguments)).Distinct().Count(),
            calls.GroupBy(call => call.Name, StringComparer.Ordinal).OrderByDescending(group => group.Count())
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            approvals, denied, prompts,
            totals?.Tokens.InputTokens ?? 0, totals?.Tokens.OutputTokens ?? 0, totals?.Tokens.CachedInputTokens ?? 0,
            totals?.Tokens.ReasoningTokens ?? 0, totals?.Requests ?? 0, totals?.Cost,
            (usage?.ByPurpose ?? []).ToDictionary(slice => slice.Key, slice => slice.Totals.Requests, StringComparer.Ordinal),
            (usage?.ByPurpose ?? []).Where(slice => slice.Key is "Compaction" or "Checkpoint").Sum(slice => slice.Totals.Requests),
            changes, checks, answer);
        await File.WriteAllTextAsync(Path.Combine(resultDirectory, "result.json"), JsonSerializer.Serialize(result, Indented), token);
        Keep(after, changes, Path.Combine(resultDirectory, "changed"));
        Keep(data, Path.Combine(resultDirectory, "data"));
        TryDelete(scratch);
        return result;
    }

    private sealed record Ended(ChatRunStatus Status, string? Error, int Approvals, int Denied, int Prompts);

    // The snapshot is polled rather than streamed: the events stream sends deltas a client has to
    // fold, while one list of runs every half second is all an evaluation needs to see.
    private static async Task<Ended> DriveAsync(EvalHost host, Scenario scenario, ChatRunSnapshot run, Trace trace, CancellationToken token)
    {
        int approvals = 0, denied = 0, prompts = 0;
        var answeredApprovals = new HashSet<Guid>();
        var answeredPrompts = new HashSet<Guid>();
        var seen = "";
        var current = run;
        // Right after a submit the snapshot can still show how the previous turn ended, or Idle before
        // the dispatcher picks the message up; the turn ends only once its own run has moved.
        var started = false;
        var idlePolls = 0;
        while (true)
        {
            var state = $"{current.Status}|{current.PendingApproval?.Id}|{current.PendingPrompt?.Id}|{string.Join(',', current.ActiveTools?.Select(tool => tool.Name) ?? [])}|{current.Wait?.Attempt}|{current.Error}";
            if (state != seen)
            {
                seen = state;
                trace.Write(new
                {
                    kind = "run", status = current.Status.ToString(), activeTools = current.ActiveTools?.Select(tool => tool.Name),
                    approval = current.PendingApproval?.Name, prompt = current.PendingPrompt?.Questions.Select(question => question.Text),
                    wait = current.Wait, error = current.Error, failure = current.FailureCode.ToString(),
                    context = current.Context
                });
            }

            started |= current.Status is ChatRunStatus.Generating or ChatRunStatus.Paused || current.Revision > run.Revision;
            if (current.PendingApproval is { } approval && answeredApprovals.Add(approval.Id))
            {
                // A scenario names a tool as its server does (process_run) or as the model sees it (mcp_built_in__process_run).
                var deny = scenario.DenyTools?.Any(name => string.Equals(approval.Name, name, StringComparison.OrdinalIgnoreCase)
                    || approval.Name.EndsWith("__" + name, StringComparison.OrdinalIgnoreCase)) == true;
                approvals++;
                if (deny) denied++;
                trace.Write(new { kind = "approval", tool = approval.Name, arguments = approval.Arguments, decision = deny ? "deny" : "allow" });
                await host.DecideAsync(current, new ToolApprovalDecision(approval.Id, deny ? ToolApprovalAction.Deny : ToolApprovalAction.Allow), token);
            }
            else if (current.PendingPrompt is { } prompt && answeredPrompts.Add(prompt.Id))
            {
                prompts++;
                var response = Answer(prompt, scenario.Prompts ?? []);
                trace.Write(new { kind = "prompt", questions = prompt.Questions, response });
                await host.AnswerAsync(current, response, token);
            }
            // A failed or interrupted run keeps its message queued for a retry, which only a person starts.
            else if ((started || ++idlePolls > 60) && (current.Status is ChatRunStatus.Failed or ChatRunStatus.Interrupted
                     || current.Status is ChatRunStatus.Completed or ChatRunStatus.Idle && current.Queue.Count == 0))
            {
                return new Ended(current.Status == ChatRunStatus.Idle ? ChatRunStatus.Completed : current.Status,
                    current.Error, approvals, denied, prompts);
            }

            await Task.Delay(Poll, token);
            current = await host.GetRunAsync(current.ChatId, token) ?? current with { Status = ChatRunStatus.Idle, Queue = [] };
        }
    }

    /// <summary>The simulated user's answer: a matching rule per question, else the recommended option, else the model's call.</summary>
    public static UserPromptResponse Answer(UserPrompt prompt, IReadOnlyList<PromptRule> rules)
    {
        var answers = new List<UserPromptAnswer>();
        foreach (var question in prompt.Questions)
        {
            var rule = rules.FirstOrDefault(item => Regex.IsMatch(question.Text, item.Match, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)));
            if (rule is { Dismiss: true }) continue;
            var option = rule?.Option is { } pattern
                ? IndexOf(question, option => Regex.IsMatch(option.Label, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                : rule?.Answer is { } text && !question.AllowOther
                    ? IndexOf(question, option => string.Equals(option.Label, text, StringComparison.OrdinalIgnoreCase))
                    : rule is null ? IndexOf(question, option => option.Recommended) : -1;
            if (option >= 0) answers.Add(new UserPromptAnswer(question.Id, [option], null));
            else if (rule?.Answer is { } other && question.AllowOther) answers.Add(new UserPromptAnswer(question.Id, [], other));
        }

        return new UserPromptResponse(prompt.Id, answers.Count == 0 ? UserPromptOutcome.Dismissed : UserPromptOutcome.Answered, answers);
    }

    private static int IndexOf(UserPromptQuestion question, Func<UserPromptOption, bool> predicate)
    {
        for (var index = 0; index < question.Options.Count; index++)
            if (predicate(question.Options[index])) return index;
        return -1;
    }

    // The answer a person reads at the end: the last assistant message with text on the main branch.
    private static string? FinalAnswer(IReadOnlyList<ChatMessageView> messages)
    {
        var byId = messages.ToDictionary(message => message.Id);
        var head = messages.LastOrDefault(message => Role(message, "Assistant") && message.Sender is null);
        for (var message = head; message is not null; message = message.ParentId is { } parent && byId.TryGetValue(parent, out var next) ? next : null)
            if (Role(message, "Assistant") && !string.IsNullOrWhiteSpace(message.Content)) return message.Content;
        return null;
    }

    private static bool Role(ChatMessageView message, string role) => string.Equals(message.Role, role, StringComparison.OrdinalIgnoreCase);

    private static void Keep(Workspace after, IReadOnlyList<string> changes, string destination)
    {
        foreach (var change in changes.Where(change => !change.StartsWith('D')))
        {
            var target = Path.Combine(destination, change[2..]);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(after.Root, change[2..]), target, true);
        }
    }

    // The Host's own record — logs, chat files, the usage ledger — without the protected key.
    private static void Keep(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.StartsWith("credentials", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try { File.Copy(file, target, true); }
            catch (IOException) { }
        }
    }

    private static void TryDelete(string directory)
    {
        try { Directory.Delete(directory, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private sealed class Trace(string path)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public void Write(object entry)
        {
            var line = JsonSerializer.Serialize(new { ms = _clock.ElapsedMilliseconds, entry }, Line);
            lock (this) File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}

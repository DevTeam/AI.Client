namespace AI.Application.Skills;

using System.Text;
using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Contracts.Projects;
using AI.Contracts.Skills;
using Microsoft.Extensions.Logging;

/// <summary>
/// One model call without tools: the user's latest message, a bounded skill shortlist and the tool list go
/// in, the ids of the fitting skills and the names of the first tools come out. The chat model then
/// meets its request with the choice already made, instead of a catalog it can overlook.
/// </summary>
public sealed class SkillRouteSkill(
    ISkillCatalog catalog,
    ISkillGuide guide,
    ISkillCandidateSelector selector,
    IChatService chats,
    IProjectService projects,
    IGlobalSettingsRepository settings,
    IGlobalSecretStore secrets,
    IChatCompletionClient completion,
    IChatContextPlanner contextPlanner,
    ILogger<SkillRouteSkill> logger) : ISkillExecutor
{
    public const string Id = "skill-route";

    public string SkillId => Id;

    public const int MaxSkills = 2;
    public const int MaxTools = 8;
    public const int MaxCandidates = 12;

    /// <summary>The catalog forms the router can choose from; a family alone names no id it could return.</summary>
    private static readonly SkillCatalogForm[] RoutedForms = [SkillCatalogForm.Full, SkillCatalogForm.Brief, SkillCatalogForm.Ids];

    /// <summary>The user is waiting on this before the first step of every turn; past it the turn goes on without a route.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(12);

    private static readonly Action<ILogger, Guid, Exception?> SkillFailed =
        LoggerMessage.Define<Guid>(LogLevel.Warning, new EventId(1603, "SkillRouteSkillFailed"),
            "Skill routing failed for project {ProjectId}");

    private static readonly Action<ILogger, string, int, Exception?> CatalogForm =
        LoggerMessage.Define<string, int>(LogLevel.Debug, new EventId(1604, "SkillRouteCatalogForm"),
            "Skill routing offers the {Form} catalogue of {Count} skills");

    public async Task<SkillExecutionResult> RunAsync(SkillInvocation invocation, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);
        try
        {
            return await RunCoreAsync(invocation, deadline.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SkillExecutionResult("Cancelled", "Skill routing was cancelled.", invocation.CurrentChatId);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return new SkillExecutionResult("Failed", "Skill routing timed out.", invocation.CurrentChatId);
        }
        catch (Exception error)
        {
            SkillFailed(logger, invocation.ProjectId, error);
            return new SkillExecutionResult("Failed", error.Message, invocation.CurrentChatId);
        }
    }

    private async Task<SkillExecutionResult> RunCoreAsync(SkillInvocation invocation, CancellationToken token)
    {
        var skill = await catalog.GetByIdAsync(Id, invocation.ProjectId, token);
        if (skill is not { Enabled: true }) return new("Failed", "The skill routing skill is unavailable.", invocation.CurrentChatId);
        var parameters = invocation.Parameters;
        var tools = parameters.TryGetProperty("tools", out var given)
            ? given.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(item => item.Length > 0).ToArray()
            : [];
        var available = (await guide.EffectiveAsync(invocation.ProjectId, token))
            .Where(item => item.Kind != SkillKinds.Executor || item.Id == "chat-rename")
            .ToArray();
        if (available.Length == 0 && tools.Length == 0)
            return new("Skipped", "There is nothing to route to.", invocation.CurrentChatId);

        var project = await projects.GetAsync(invocation.ProjectId, token);
        var connection = await ConnectionAsync(invocation, project, token);
        if (connection is null) return new("Failed", "No enabled model connection is available.", invocation.CurrentChatId);
        var message = Text(parameters, "message");
        // The whole catalog goes to the router in the most detailed form that fits: the router reads
        // the user's message in any language, which a word match over English metadata cannot.
        // Only when not even the ids fit does a word match pick the few candidates it sees.
        var entries = available.Select(SkillCatalogEntry.Of).ToArray();
        var offers = RoutedForms.Select(form => (Form: form.ToString(), Skills: (IReadOnlyList<SkillDefinition>)available,
                Lines: SkillCatalogText.Lines(entries, form)))
            .Append(Fallback(selector.FindCandidates(message ?? string.Empty, available, MaxCandidates, Text(parameters, "active_skill"))));
        ChatCompletionRequest? request = null;
        IReadOnlyList<SkillDefinition> offered = [];
        foreach (var offer in offers)
        {
            var conversation = new List<ChatCompletionMessage>
            {
                new("system", SkillMarkdown.Body(skill.Content)),
                new("user", JsonSerializer.Serialize(new
                {
                    message,
                    previous = Text(parameters, "previous"),
                    active_skill = Text(parameters, "active_skill"),
                    directories = project?.DirectoryGrants.Select(GrantText) ?? [],
                    skills = offer.Lines,
                    tools
                }))
            };
            var plan = contextPlanner.Plan(connection, connection.Model, conversation, []);
            if (!plan.Fits) continue;
            CatalogForm(logger, offer.Form, offer.Skills.Count, null);
            request = new ChatCompletionRequest(connection.BaseUrl, connection.Model,
                await secrets.GetAsync("connection", connection.Id, token), "Route the request", connection.Id, conversation,
                ReasoningEffort: connection.ReasoningEffort) { ContextMessages = plan.Messages };
            offered = offer.Skills;
            break;
        }
        if (request is null)
            return new("Skipped", "The routing catalogue exceeds this connection's context budget; use progressive discovery in the chat.", invocation.CurrentChatId);
        var content = new StringBuilder();
        await foreach (var chunk in completion.StreamAsync(request, token)) content.Append(chunk.Content);
        var (skills, chosenTools) = Parse(content.ToString(),
            offered.Select(item => item.Id).ToHashSet(StringComparer.Ordinal),
            tools.Select(ToolName).ToHashSet(StringComparer.Ordinal));
        return new("Completed", skills.Count == 0 ? "No skill fits the message." : "Routed to " + string.Join(", ", skills) + ".",
            invocation.CurrentChatId, Output: JsonSerializer.SerializeToElement(new { skills, tools = chosenTools }));
    }

    /// <summary>
    /// The model's answer is read leniently, because a small model wraps JSON in a fence or a
    /// sentence, and strictly, because only known ids and names survive: a guessed skill would
    /// send the chat model to run something that does not exist.
    /// </summary>
    private static (IReadOnlyList<string> Skills, IReadOnlyList<string> Tools) Parse(string content,
        IReadOnlySet<string> skillIds, IReadOnlySet<string> toolNames)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return ([], []);
        try
        {
            using var document = JsonDocument.Parse(content[start..(end + 1)]);
            return (Names(document.RootElement, "skills", skillIds, MaxSkills),
                Names(document.RootElement, "tools", toolNames, MaxTools));
        }
        catch (JsonException)
        {
            return ([], []);
        }
    }

    private static string[] Names(JsonElement root, string property, IReadOnlySet<string> known, int limit) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!.Trim()).Where(known.Contains).Distinct(StringComparer.Ordinal).Take(limit).ToArray()
            : [];

    /// <summary>A tool is offered as "name: description"; the name is the part the answer repeats.</summary>
    private static string ToolName(string tool) => tool.Split(':', 2)[0].Trim();

    /// <summary>The last resort for a catalog too large for the connection even as ids: the candidates a word match finds.</summary>
    private static (string Form, IReadOnlyList<SkillDefinition> Skills, IReadOnlyList<string> Lines) Fallback(
        IReadOnlyList<SkillDefinition> candidates) =>
        ("Candidates", candidates, SkillCatalogText.Lines(candidates.Select(SkillCatalogEntry.Of).ToArray(), SkillCatalogForm.Brief));

    /// <summary>
    /// A directory the project can already reach, as "root: tools[, recursive]". Without it a message
    /// that only names a path ("study C:\repo") reads like a request to grant access to it.
    /// </summary>
    private static string GrantText(DirectoryGrantSettings grant) =>
        $"{grant.CanonicalRoot}: {string.Join(", ", grant.ToolNames)}{(grant.Recursive ? ", recursive" : "")}";

    private static string? Text(JsonElement parameters, string property) =>
        parameters.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>The chat's own model, as <see cref="ChatReplySuggestSkill"/> picks it.</summary>
    private async Task<AI.Contracts.Settings.ConnectionSettings?> ConnectionAsync(SkillInvocation invocation,
        ProjectDetails? project, CancellationToken token)
    {
        var global = await settings.LoadAsync(token);
        var chat = invocation.CurrentChatId is { } chatId ? await chats.GetAsync(invocation.ProjectId, chatId, token) : null;
        var connectionId = chat?.ConnectionId ?? project?.ConnectionId;
        // The person waits on this before every turn, and picking from a short list is the kind of
        // work the connections marked for subtasks are for: usually a cheaper, faster model.
        return global.Connections.FirstOrDefault(item => item.ForSubtasks && item.Enabled)
            ?? global.Connections.FirstOrDefault(item => item.Id == connectionId && item.Enabled)
            ?? global.Connections.FirstOrDefault(item => item.IsDefault && item.Enabled)
            ?? global.Connections.FirstOrDefault(item => item.Enabled);
    }
}

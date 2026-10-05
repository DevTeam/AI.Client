namespace AI.Application.Tools;

using System.Text.Json;
using AI.Application.Chats;
using AI.Application.Skills;
using AI.Contracts.Chats;

/// <param name="Allowed">True when the call may run without asking the person.</param>
/// <param name="Reason">
/// What the risk assessment said, when one ran: why the call was let through, or what the person
/// should look at on the card they are about to be shown.
/// </param>
public sealed record ToolAutoApproval(bool Allowed, string? Reason = null)
{
    public static readonly ToolAutoApproval Ask = new(false);
}

/// <summary>
/// Answers, for the chat's approval mode, a call its standing policy leaves at Ask — before any
/// card is shown. It never overrides Deny or the call limits: it is only reached for Ask.
/// </summary>
public interface IToolAutoApprover
{
    Task<ToolApprovalMode> ModeAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);

    Task<ToolAutoApproval> DecideAsync(Guid projectId, Guid chatId, Guid branchId, AgentTool tool, string arguments,
        CancellationToken cancellationToken);
}

public sealed class ToolAutoApprover(IChatService chats, ISkillRunner skills) : IToolAutoApprover
{
    public async Task<ToolApprovalMode> ModeAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken) =>
        (await chats.GetAsync(projectId, chatId, cancellationToken))?.ApprovalMode ?? ToolApprovalMode.Ask;

    public async Task<ToolAutoApproval> DecideAsync(Guid projectId, Guid chatId, Guid branchId, AgentTool tool,
        string arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var mode = await ModeAsync(projectId, chatId, cancellationToken);
        if (mode == ToolApprovalMode.FullAccess) return new ToolAutoApproval(true);
        var assessment = await AssessAsync(projectId, chatId, branchId, tool, arguments, cancellationToken);
        return mode == ToolApprovalMode.Auto ? assessment : assessment with { Allowed = false };
    }

    private async Task<ToolAutoApproval> AssessAsync(Guid projectId, Guid chatId, Guid branchId, AgentTool tool,
        string arguments, CancellationToken cancellationToken)
    {
        var descriptor = tool.Descriptor;
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["chat_id"] = chatId.ToString(),
            ["branch_id"] = branchId.ToString(),
            ["tool_name"] = tool.OriginalName,
            ["arguments"] = arguments
        };
        if (descriptor.DisplayName != tool.OriginalName) parameters["tool_title"] = descriptor.DisplayName;
        if (descriptor.Description is { Length: > 0 } description) parameters["tool_description"] = description;
        if (descriptor.Annotations is { } annotations)
            parameters["annotations"] = JsonSerializer.Serialize(new
            {
                readOnly = annotations.ReadOnlyHint,
                destructive = annotations.DestructiveHint,
                idempotent = annotations.IdempotentHint,
                openWorld = annotations.OpenWorldHint
            });
        var record = await skills.RunAsync(new SkillInvocation(ChatToolRiskAssessSkill.Id, projectId,
            JsonSerializer.SerializeToElement(parameters), chatId, branchId), cancellationToken);
        // A failed, cancelled or unreadable assessment is not a verdict, so the person is asked.
        if (record.Status != "Completed" || record.Output is not { ValueKind: JsonValueKind.Object } output)
            return ToolAutoApproval.Ask;
        var reason = output.TryGetProperty("reason", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString()
            : null;
        var allowed = output.TryGetProperty("decision", out var decision) && decision.ValueKind == JsonValueKind.String
            && decision.GetString() == "allow";
        return new ToolAutoApproval(allowed, reason);
    }
}

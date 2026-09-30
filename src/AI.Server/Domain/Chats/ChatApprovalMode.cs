namespace AI.Domain.Chats;

/// <summary>Who answers a tool call the chat's policy leaves at Ask; see <c>ToolApprovalMode</c>.</summary>
public enum ChatApprovalMode
{
    Ask,
    Auto,
    FullAccess
}

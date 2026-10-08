namespace AI.Contracts.Settings;

/// <summary>Conservative defaults for known Host tools; unknown and external tools require approval.</summary>
public sealed class ToolDefaultDecision : IToolDefaultDecision
{
    public string GetDecision(Guid serverId, string name)
    {
        if (serverId == DefaultMcpServer.Id && name is
            "list_allowed_directories" or "read_text_file" or "read_multiple_files" or "read_image_file"
            or "list_directory" or "directory_tree" or "search_files" or "grep_files" or "get_file_info"
            or "zip_list" or "zip_read" or "trigger_wait")
            return "Allow";

        if (serverId == AppMcpServer.Id && name is
            "app_read" or "tool_search" or "skill_search" or "ask_user" or "app_navigate")
            return "Allow";

        return "Ask";
    }
}

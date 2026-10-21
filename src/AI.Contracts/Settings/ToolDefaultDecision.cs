namespace AI.Contracts.Settings;

/// <summary>Conservative defaults for known Host tools; unknown and external tools require approval.</summary>
public sealed class ToolDefaultDecision : IToolDefaultDecision
{
    /// <summary>The built-in C# script tool, allowed by default while scripts run in the sandbox.</summary>
    public const string ScriptTool = "cs_run";

    public string GetDecision(Guid serverId, string name, ToolSandboxSettings? sandbox = null)
    {
        if (serverId == DefaultMcpServer.Id && name is
            "list_allowed_directories" or "read_text_file" or "read_multiple_files" or "read_image_file"
            or "list_directory" or "directory_tree" or "search_files" or "grep_files" or "get_file_info"
            or "zip_list" or "zip_read" or "trigger_wait")
            return "Allow";

        // A sandboxed script reaches the grants and the chat's temporary directory and nothing else,
        // the network included where the OS can close it, so running one unasked gives no more than
        // the file tools already do. It is also what lets subtasks, which cannot ask, use scripts.
        // Outside the sandbox a script can reach the whole account, so it is asked about.
        if (serverId == DefaultMcpServer.Id && name == ScriptTool)
            return (sandbox ?? new ToolSandboxSettings()).Scripts ? "Allow" : "Ask";

        if (serverId == AppMcpServer.Id && name is
            "app_read" or "tool_search" or "skill_search" or "ask_user" or "app_navigate" or "plan_update")
            return "Allow";

        return "Ask";
    }
}

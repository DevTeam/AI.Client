namespace AI.Contracts.Settings;

/// <summary>Conservative defaults for known Host tools; unknown and external tools require approval.</summary>
public sealed class ToolDefaultDecision : IToolDefaultDecision
{
    public string GetDecision(Guid serverId, string name)
    {
        if (serverId == DefaultMcpServer.Id && name is
            "list_allowed_directories" or "read_text_file" or "read_multiple_files" or "read_image_file"
            or "list_directory" or "directory_tree" or "search_files" or "grep_files" or "get_file_info"
            or "zip_list" or "zip_read" or "trigger_wait" or "process_wait" or "process_stop")
            return "Allow";

        // Commands and scripts always run in the sandbox, which reaches the grants and the chat's
        // temporary directory and nothing else, so running them unasked gives no more than the file
        // tools already do; it is also what lets subtasks, which cannot ask, run them at all.
        // process_run_unrestricted is the way past the sandbox and is asked about.
        if (serverId == DefaultMcpServer.Id && name is CommandRule.ProcessRun or "cs_run")
            return "Allow";

        if (serverId == AppMcpServer.Id && name is
            "app_read" or "tool_search" or "skill_search" or "ask_user" or "app_navigate" or "plan_update")
            return "Allow";

        return "Ask";
    }
}

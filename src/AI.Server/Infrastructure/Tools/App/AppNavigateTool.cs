namespace AI.Mcp.App;

using AI.Application.Chats;
using AI.Application.Notifications;
using AI.Application.Projects;
using AI.Application.Tools;
using AI.Contracts.Navigation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

public sealed record AppNavigateResult(bool Opened, Guid ProjectId, Guid? ChatId, Guid? BranchId, string Effect,
    string? Error = null);

/// <summary>
/// Shows the user a project, chat or branch, as a click in the sidebar would. It changes only what
/// the window shows: nothing is created, and the run that called it keeps running in its own chat.
/// </summary>
[McpServerToolType]
public sealed class AppNavigateTool(IProjectService projects, IChatService chats, IAppNavigationSignal navigation) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) =>
        new Session(projects, chats, navigation, run, reply).Create();

    private sealed class Session(IProjectService projects, IChatService chats, IAppNavigationSignal navigation,
        ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(NavigateAsync,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "Open a project, one of its chats or a branch in the user's window, as if they had clicked it. "
                              + "Use it right after you create a project, chat or branch the user is meant to continue in, "
                              + "or when they ask to be taken somewhere; never to show them what you are reading. It changes "
                              + "nothing else, and this run keeps running in its own chat. Omit chatId to open the project; "
                              + "omit branchId for the main branch."
            });

        [McpServerTool(Name = "app_navigate", ReadOnly = false, Destructive = false, Idempotent = true,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(AppNavigateResult))]
        private async Task<CallToolResult> NavigateAsync(Guid projectId, Guid? chatId = null, Guid? branchId = null,
            CancellationToken cancellationToken = default)
        {
            AppNavigateResult Failed(string error) => new(false, projectId, chatId, branchId, "Nothing was opened.", error);
            // A background run has nobody at a window, and moving whoever opens one later would be a surprise.
            if (!run.Interactive)
                return reply.Reply(Failed("This run is not attached to the user's window."), true);
            var project = await projects.GetAsync(projectId, cancellationToken);
            if (project is null) return reply.Reply(Failed("Project not found."), true);
            if (branchId is not null && chatId is null)
                return reply.Reply(Failed("A branch needs its chatId."), true);
            var title = $"project '{project.Name}'";
            string? chatTitle = null;
            if (chatId is { } id)
            {
                var chat = await chats.GetAsync(projectId, id, cancellationToken);
                if (chat is null) return reply.Reply(Failed("Chat not found in that project."), true);
                if (branchId is { } branch && branch != id && chat.Branches?.Any(item => item.Id == branch) != true)
                    return reply.Reply(Failed("Branch not found in that chat."), true);
                chatTitle = chat.Title;
                title = branchId is { } shown && shown != id ? $"a branch of chat '{chat.Title}'" : $"chat '{chat.Title}'";
            }
            navigation.Navigate(new AppNavigation(projectId, chatId, branchId == chatId ? null : branchId, run.ChatId,
                project.Name, chatTitle));
            return reply.Reply(new AppNavigateResult(true, projectId, chatId, branchId, $"Opened {title}."));
        }
    }
}

namespace AI.Server.Hosting.Endpoints;

using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Runs;
using AI.Application.Settings;
using AI.Application.Skills;
using AI.Contracts.Chats;
using AI.Contracts.Navigation;
using AI.Contracts.Runs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public sealed class AppGuideEndpoints(IAppGuideTopics topics, IAppGuideLanguageContext languageContext, IGuideChats guideChats) : IEndpointModule
{
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/guide/start", async (AppGuideStartRequest request,
            ISkillCatalog skills, IProjectService projects, IChatService chats,
            IChatRunDispatcher runs, IGlobalSettingsService settings, CancellationToken token) =>
        {
            var topic = topics.Find(request.Topic);
            if (topic is null || request.Mode is not ("show" or "click")) return Results.BadRequest("Unknown guide topic or mode.");
            if (request.Language?.Length > 80 || request.UiLocale?.Length > 80) return Results.BadRequest("Language identifier is too long.");
            if (await projects.GetAsync(request.ProjectId, token) is not { } project) return Results.NotFound();
            var skill = await skills.GetByIdAsync(topic.SkillId, request.ProjectId, token);
            if (skill is not { Enabled: true }) return Results.BadRequest("This guide skill is unavailable.");
            var visibleChat = request.ChatId is { } visibleId ? await chats.GetTranscriptAsync(request.ProjectId, visibleId, token) : null;
            // No hidden chat is created for a tour that could not run: it would only fail and stay behind.
            if (guideChats.PickConnection(await settings.GetAsync(token), visibleChat?.ConnectionId, project.ConnectionId) is not { } connectionId)
                return Results.Conflict("No model connection is enabled.");
            // Finished tours are removed here as well, in case a window closed before it cleaned up.
            await guideChats.CleanUpAsync(request.ProjectId, null, token);
            var chat = await chats.CreateAsync(request.ProjectId,
                new CreateChatRequest($"Guide · {topic.Title}", connectionId, IsGuide: true, GuideMode: request.Mode), token);
            var prompt = languageContext.Create(request, visibleChat)
                + $"Run the built-in application guide '{topic.SkillId}' in this hidden service chat. "
                + $"The visible project is {request.ProjectId}, visible chat is {request.ChatId?.ToString() ?? "none"}, "
                + $"visible branch is {request.BranchId?.ToString() ?? "main"}. Demonstration mode: {request.Mode}. "
                + "Do not navigate to this service chat. Questions must use presentation='overlay'. "
                + "Use only application navigation/discovery tools and ask_user; do not read files or run commands. "
                + "Every navigation step must have waitForContinue=true and a concise comment in the user's language. "
                + "Use timeoutSeconds=15 for every visible navigation step; the window automatically continues a shown guide step. "
                + "Stop immediately on stopped or expired navigation or any unanswered outside-chat question. "
                + "An unavailable result means the control is not in the current view (a new chat has no messages yet): "
                + "do not stop; say it cannot be shown yet, ask the person to bring it up and wait, as the tool result explains; "
                + "for a topic that needs a conversation, offer a demo chat (target='chat.demo'). "
                + "When something does not work, do not push on: ask whether the person wants to try it themselves "
                + "or wants help fixing it, as the skill describes. "
                + "Never ask to start this guide again: the user already started it. "
                + "Ask what the person wants to learn unless that interest was explicitly supplied. "
                + "After two or three useful steps, offer a choice when useful; a block length alone does not mean a topic is exhausted. "
                + "Questions must have allowOther=true and invite the person to write their own interest. "
                + "Opening interests and questions within an unfinished topic must explicitly use timeoutSeconds=0, timeoutBehavior='cancel'. "
                + "Recommend a useful continuation, never Stop by default in the middle of a topic. "
                + "Only at an important topic fork after the current branch and its takeaway are complete may a question use "
                + "timeoutSeconds=30, timeoutBehavior='cancel', with Finish recommended; no answer ends the completed tour. "
                + "At such a boundary a free-text choice can still wait without a timer. Do not choose or advance while awaiting an answer. "
                + "Teach visually through app_navigate comments beside relevant controls, not chat messages. "
                + "Use aiclient://navigate Markdown shortcuts in visible step comments and question text, including a useful link in the final takeaway. "
                + "Hidden service-chat messages are not visible. Links are optional navigation, never Continue, an answer or authorization to change anything. "
                + "Chat text may contain only brief progress markers and one short completion phrase. "
                + "Put takeaways in the final visible step. Follow these skill instructions:\n\n" + skill.Content;
            var snapshot = await runs.SubmitAsync(request.ProjectId, chat.Id,
                new SubmitChatMessageRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), prompt), token);
            return Results.Ok(snapshot);
        });
        // The window that ran a tour removes its service chat once it has read how the tour ended.
        routes.MapPost("/api/guide/{projectId:guid}/cleanup", async (Guid projectId, CancellationToken token) =>
            Results.Ok(await guideChats.CleanUpAsync(projectId, null, token)));
    }
}

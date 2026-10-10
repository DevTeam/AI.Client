namespace AI.Server.Hosting.Endpoints;

using Application.Schedules;
using Contracts.Schedules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>The schedule of a chat for the Web's schedule widget; the same service the app_schedule tool uses.</summary>
public sealed class ScheduleEndpoints : IEndpointModule
{
    private const string Route = "/api/projects/{projectId:guid}/chats/{chatId:guid}/schedule";

    public void Map(IEndpointRouteBuilder routes)
    {
        const string branchRoute = "/api/projects/{projectId:guid}/chats/{chatId:guid}/branches/{branchId:guid}/schedule";
        routes.MapGet(branchRoute, async (Guid projectId, Guid chatId, Guid branchId, IChatScheduleService schedules, CancellationToken token) =>
            Found(await schedules.GetAsync(projectId, chatId, branchId, token)));
        routes.MapPut(branchRoute, async (Guid projectId, Guid chatId, Guid branchId, SetChatScheduleRequest request,
            IChatScheduleService schedules, CancellationToken token) =>
            await GuardAsync(() => schedules.SetAsync(projectId, chatId, branchId, request, token)));
        routes.MapDelete(branchRoute, async (Guid projectId, Guid chatId, Guid branchId, IChatScheduleService schedules, CancellationToken token) =>
            Found(await schedules.RemoveAsync(projectId, chatId, branchId, token)));
        routes.MapPost(branchRoute + "/pause", async (Guid projectId, Guid chatId, Guid branchId, long? revision,
            IChatScheduleService schedules, CancellationToken token) =>
            await GuardAsync(() => schedules.PauseAsync(projectId, chatId, branchId, true, revision, token)));
        routes.MapPost(branchRoute + "/resume", async (Guid projectId, Guid chatId, Guid branchId, long? revision,
            IChatScheduleService schedules, CancellationToken token) =>
            await GuardAsync(() => schedules.PauseAsync(projectId, chatId, branchId, false, revision, token)));
        routes.MapPost(branchRoute + "/run", async (Guid projectId, Guid chatId, Guid branchId,
            IChatScheduleService schedules, CancellationToken token) =>
            Found(await schedules.RunNowAsync(projectId, chatId, branchId, token)));
        routes.MapGet(Route, async (Guid projectId, Guid chatId, IChatScheduleService schedules, CancellationToken token) =>
            Found(await schedules.GetAsync(projectId, chatId, token)));
        routes.MapPut(Route, async (Guid projectId, Guid chatId, SetChatScheduleRequest request, IChatScheduleService schedules,
            CancellationToken token) => await GuardAsync(() => schedules.SetAsync(projectId, chatId, request, token)));
        routes.MapDelete(Route, async (Guid projectId, Guid chatId, IChatScheduleService schedules, CancellationToken token) =>
            Found(await schedules.RemoveAsync(projectId, chatId, token)));
        routes.MapPost(Route + "/pause", async (Guid projectId, Guid chatId, long? revision, IChatScheduleService schedules,
            CancellationToken token) => await GuardAsync(() => schedules.PauseAsync(projectId, chatId, true, revision, token)));
        routes.MapPost(Route + "/resume", async (Guid projectId, Guid chatId, long? revision, IChatScheduleService schedules,
            CancellationToken token) => await GuardAsync(() => schedules.PauseAsync(projectId, chatId, false, revision, token)));
        routes.MapPost(Route + "/run", async (Guid projectId, Guid chatId, IChatScheduleService schedules, CancellationToken token) =>
            Found(await schedules.RunNowAsync(projectId, chatId, token)));
        MapSoon(routes);
    }

    private static IResult Found(ChatScheduleView? view) => view is null ? Results.NotFound() : Results.Ok(view);

    /// <summary>
    /// The sidebar's Scheduled: the scheduled chats of every project that come due within the next
    /// day, soonest first, so a run that is about to start is visible before it starts.
    /// </summary>
    private static readonly string SoonRoute = "/api/scheduled-chats/soon";

    private static void MapSoon(IEndpointRouteBuilder routes) =>
        routes.MapGet(SoonRoute, async (int? limit, int? horizonMinutes, IScheduledChatQuery query,
            CancellationToken token) =>
            Results.Ok(await query.SoonAsync(
                TimeSpan.FromMinutes(Math.Clamp(horizonMinutes ?? ScheduledChats.DefaultHorizonMinutes,
                    ScheduledChats.MinHorizonMinutes, ScheduledChats.MaxHorizonMinutes)),
                Math.Clamp(limit ?? ScheduledChats.DefaultCount, ScheduledChats.MinCount, ScheduledChats.MaxCount),
                token)));

    /// <summary>A stale revision answers 409 with the schedule as it is now, so the widget can show it.</summary>
    private static async Task<IResult> GuardAsync(Func<Task<ChatScheduleView?>> change)
    {
        try
        {
            return Found(await change());
        }
        catch (ScheduleConflictException conflict)
        {
            return Results.Conflict(conflict.Current);
        }
    }
}

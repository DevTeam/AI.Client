namespace AI.Mcp.App;

using System.Text.Json;
using AI.Application.Schedules;
using AI.Application.Tools;
using AI.Contracts.Schedules;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

public enum ScheduleOperation
{
    /// <summary>Read the chat's schedule, its next run and its recent runs. Changes nothing.</summary>
    Get,

    /// <summary>Create or replace the selected branch's schedule from 'settings'.</summary>
    Set,

    /// <summary>Stop starting runs until Resume. The run going now keeps going.</summary>
    Pause,

    /// <summary>Start runs again; occurrences that passed while paused are skipped.</summary>
    Resume,

    /// <summary>Remove the selected branch's schedule, keeping messages and branches.</summary>
    Remove,

    /// <summary>Start a run now, outside the recurrence.</summary>
    RunNow,

    /// <summary>From a scheduled run branch: report whether the run succeeded, with 'succeeded', 'summary' and, on failure, 'retry'.</summary>
    ReportRun,
}

/// <summary>
/// The schedule of a branch: each run forks from that branch's current head and follows the
/// <c>chat-schedule-run</c> skill.
/// </summary>
[McpServerToolType]
public sealed class AppScheduleTool(IChatScheduleService schedules, IAppWrites writes, IScheduleCalendar calendar) : IAppTool
{
    private string LocalTimeZone => calendar.LocalTimeZoneId;

    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(this, run, reply).Create();

    private sealed class Session(AppScheduleTool tool, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(
            ScheduleAsync,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "Read and change a branch schedule. At each occurrence the application forks the owner branch "
                              + "from its current head and the run carries out 'settings.task'. projectId, chatId and branchId "
                              + "default to the current branch. Set creates or replaces only that branch's schedule; "
                              + "an empty main branch gets the task as its first message. settings.recurrence is "
                              + "{frequency: Once|Hourly|Daily|Weekly|Monthly|Yearly, start:'yyyy-MM-dd', time:'HH:mm', interval, weekdays:[Monday…], "
                              + "monthDay (1–31, -1 last), monthWeekday:{ordinal 1–4 or -1, day}, until:'yyyy-MM-dd', count} in settings.timeZone "
                              + $"(IANA id; empty means the host's '{tool.LocalTimeZone}'). Recurrence values from ask_user pickerKind='recurrence' "
                              + "can be passed as they are. settings.successCriteria says how a run tells success from failure; settings.retry "
                              + "{maxAttempts, delayMinutes, condition} retries failures; settings.retention {succeeded, failed, blocked} each "
                              + "{action: Keep|Delete, delayMinutes} decides what happens to run branches by outcome (blocked = waiting for a person); "
                              + "settings.deletion {at, afterLastRunMinutes} deletes the chat itself and is only valid on the main branch. Never invent a date, time, recurrence, "
                              + "success criteria, retry or deletion rule the user did not give: ask for the missing ones with ask_user "
                              + "(pickerKind 'date', 'time', 'recurrence'). Pass the schedule 'revision' you read to Set, Pause and Resume; a stale "
                              + "one changes nothing and returns the current schedule. 'operationId' must be a fresh UUID per change. "
                              + "ReportRun is for the run branch itself, at the end of a run, with succeeded, a one-line summary and, on "
                              + "failure, retry=false when the retry condition says the failure is not worth retrying."
            });

        [McpServerTool(Name = "app_schedule", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false,
            UseStructuredContent = true, OutputSchemaType = typeof(AppWriteResult))]
        private Task<CallToolResult> ScheduleAsync(
            ScheduleOperation operation, Guid operationId = default, Guid? projectId = null, Guid? chatId = null,
            Guid? branchId = null,
            ChatScheduleSettings? settings = null, long? revision = null, bool? paused = null,
            bool? succeeded = null, string? summary = null, bool? retry = null,
            CancellationToken cancellationToken = default) =>
            tool.ScheduleAsync(run, reply, operation, operationId, projectId ?? run.ProjectId, chatId ?? run.ChatId,
                branchId ?? (chatId is { } targetChat && targetChat != run.ChatId ? targetChat : run.BranchId), settings, revision,
                paused, succeeded, summary, retry, cancellationToken);
    }

    private async Task<CallToolResult> ScheduleAsync(ToolRunContext run, IAppToolReply reply, ScheduleOperation operation,
        Guid operationId, Guid projectId, Guid chatId, Guid branchId, ChatScheduleSettings? settings, long? revision, bool? paused,
        bool? succeeded, string? summary, bool? retry, CancellationToken cancellationToken)
    {
        if (operation == ScheduleOperation.Get)
        {
            var view = await schedules.GetAsync(projectId, chatId, branchId, cancellationToken);
            var builder = AppWriteBuilder.For(nameof(ScheduleOperation.Get));
            var result = view is null
                ? builder.Failed("Chat not found.", projectId, chatId)
                : new AppWriteResult(nameof(ScheduleOperation.Get), false, false,
                    view.Schedule is null ? "This branch has no schedule." : $"Schedule: {view.Description}.", projectId, chatId, null, null,
                    view.Schedule?.Revision ?? 0, "Read", Element(view, reply.Json), false, null);
            return reply.Reply(result, result.Error is not null);
        }

        return await writes.RunAsync(operation.ToString(), operationId, async builder =>
        {
            try
            {
                return operation switch
                {
                    ScheduleOperation.Set => Reply(builder, reply, projectId, chatId, await schedules.SetAsync(projectId, chatId, branchId,
                        new SetChatScheduleRequest(settings ?? throw new ArgumentException("'settings' is required for Set."), revision, paused),
                        cancellationToken), view => $"Scheduled the branch: {view.Description}."),
                    ScheduleOperation.Pause or ScheduleOperation.Resume => Reply(builder, reply, projectId, chatId,
                        await schedules.PauseAsync(projectId, chatId, branchId, operation == ScheduleOperation.Pause, revision, cancellationToken),
                        _ => operation == ScheduleOperation.Pause ? "Paused the schedule." : "Resumed the schedule."),
                    ScheduleOperation.Remove => Reply(builder, reply, projectId, chatId,
                        await schedules.RemoveAsync(projectId, chatId, branchId, cancellationToken),
                        _ => "Removed this branch's schedule."),
                    ScheduleOperation.RunNow => Reply(builder, reply, projectId, chatId,
                        await schedules.RunNowAsync(projectId, chatId, branchId, cancellationToken),
                        _ => "Asked for a run now; it starts within seconds."),
                    ScheduleOperation.ReportRun => await ReportAsync(builder, reply, run, succeeded, summary, retry, cancellationToken),
                    _ => throw new ArgumentException("Unknown operation.", nameof(operation))
                };
            }
            catch (ScheduleConflictException conflict)
            {
                return builder.Conflict(conflict.Current.Schedule?.Revision ?? 0, Element(conflict.Current, reply.Json), projectId, chatId);
            }
        });
    }

    private async Task<AppWriteResult> ReportAsync(AppWriteBuilder builder, IAppToolReply reply, ToolRunContext run, bool? succeeded,
        string? summary, bool? retry, CancellationToken cancellationToken)
    {
        var record = await schedules.ReportRunAsync(run.ProjectId, run.ChatId, run.BranchId,
            succeeded ?? throw new ArgumentException("'succeeded' is required for ReportRun."), summary, retry, cancellationToken);
        return builder.Applied($"Reported run #{record.Number} as {(succeeded.Value ? "succeeded" : "failed")}. End the turn with a short summary.",
            run.ProjectId, run.ChatId, run.BranchId, current: Element(record, reply.Json));
    }

    private static AppWriteResult Reply(AppWriteBuilder builder, IAppToolReply reply, Guid projectId, Guid chatId,
        ChatScheduleView? view, Func<ChatScheduleView, string> effect) =>
        view is null
            ? builder.Failed("Chat not found.", projectId, chatId)
            : builder.Applied(effect(view), projectId, chatId, revision: view.Schedule?.Revision ?? 0, current: Element(view, reply.Json));

    private static JsonElement Element<T>(T value, JsonSerializerOptions options) => JsonSerializer.SerializeToElement(value, options);
}

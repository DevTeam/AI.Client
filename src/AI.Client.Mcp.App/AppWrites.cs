namespace AI.Client.Mcp.App;

using AI.Client.Application.Notifications;
using ModelContextProtocol.Protocol;
using System.Text.Json;

/// <summary>
/// The parts every mutating tool shares: replay protection, the change signal, and turning an
/// outcome into a result whose shape does not depend on whether it succeeded.
/// </summary>
public sealed class AppWrites(IAppOperationLog log, IAppDataChangeSignal signal) : IAppWrites
{
    public async Task<CallToolResult> RunAsync(
        string operation,
        Guid operationId,
        Func<AppWriteBuilder, Task<AppWriteResult>> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (operationId == Guid.Empty)
            return ToolReply.Of(AppWriteBuilder.For(operation).Failed("'operationId' must be a fresh UUID."), true);

        // A repeat of an operation that already landed answers with what it did the first time.
        // Only applied mutations are remembered: a dry run and a rejected call changed nothing,
        // so there is nothing to protect against repeating.
        if (log.TryGet(operationId, out var previous) && previous is not null)
            return ToolReply.Of(previous with { Replayed = true }, previous.Error is not null);

        AppWriteResult result;
        try
        {
            result = await body(AppWriteBuilder.For(operation));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or JsonException)
        {
            result = AppWriteBuilder.For(operation).Failed(error.Message);
        }

        if (result.Applied)
        {
            log.Record(operationId, result);
            signal.Notify();
        }

        return ToolReply.Of(result, result.Error is not null);
    }
}

/// <summary>Builds the one result shape every mutation answers with.</summary>
public sealed record AppWriteBuilder(string Operation)
{
    public static AppWriteBuilder For(string operation) => new(operation);

    public AppWriteResult Applied(string effect, Guid? projectId = null, Guid? chatId = null, Guid? branchId = null,
        Guid? messageId = null, long revision = 0, string? status = null, JsonElement? current = null) =>
        new(Operation, true, false, effect, projectId, chatId, branchId, messageId, revision, status, current, false, null);

    /// <summary>A destructive call that was only described. Nothing was written and nothing is remembered.</summary>
    public AppWriteResult Planned(string effect, Guid? projectId = null, Guid? chatId = null, Guid? branchId = null,
        long revision = 0, JsonElement? current = null) =>
        new(Operation, false, true, effect, projectId, chatId, branchId, null, revision, "DryRun", current, false, null);

    /// <summary>
    /// Someone else wrote first. The caller gets the revision and the document as they are now and
    /// decides for itself whether to repeat the change — the Host never retries on its behalf.
    /// </summary>
    public AppWriteResult Conflict(long revision, JsonElement? current, Guid? projectId = null, Guid? chatId = null) =>
        new(Operation, false, false, "Nothing was changed.", projectId, chatId, null, null, revision, "Conflict", current, false,
            "The revision does not match the stored one. Re-read the resource and decide whether to repeat this change.");

    public AppWriteResult Failed(string error, Guid? projectId = null, Guid? chatId = null) =>
        new(Operation, false, false, "Nothing was changed.", projectId, chatId, null, null, 0, "Error", null, false, error);
}

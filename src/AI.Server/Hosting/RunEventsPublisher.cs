namespace AI.Server.Hosting;

using System.Text.Json;
using System.Threading.Channels;
using Application.Notifications;
using Application.Runs;
using Contracts.Runs;
using Microsoft.AspNetCore.Http;

public sealed class RunEventsPublisher(
    IChatRunDispatcher dispatcher,
    IAppDataChangeSignal changes,
    IAppNavigationSignal navigation,
    IRunSnapshotComparer comparer) : IRunEventsPublisher
{
    public async Task WriteAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        // Send the headers now rather than with the first event, which may be minutes away: the
        // client learns from them that the Host is reachable, and fetch resolves only on headers.
        await response.StartAsync(cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
        // One response, three sources. Frames are funnelled through a channel so that only this loop
        // ever writes to the body: several producers writing to one HTTP response would interleave.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var frames = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        var producers = Task.WhenAll(PublishRunsAsync(frames.Writer, stop.Token), PublishDataChangesAsync(frames.Writer, stop.Token),
                PublishNavigationAsync(frames.Writer, stop.Token))
            .ContinueWith(_ => frames.Writer.TryComplete(), TaskScheduler.Default);
        try
        {
            await foreach (var frame in frames.Reader.ReadAllAsync(cancellationToken))
            {
                await response.WriteAsync(frame, cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }
        }
        finally
        {
            await stop.CancelAsync();
            try { await producers; } catch (OperationCanceledException) { }
        }
    }

    // The signal says nothing about what moved, so the frame carries only a counter and the client
    // re-reads whatever it is showing. Bursts are held back briefly: a tool that writes ten times
    // in a second should cost the client one reload, not ten.
    private async Task PublishDataChangesAsync(ChannelWriter<string> frames, CancellationToken token)
    {
        try
        {
            await foreach (var version in changes.SubscribeAsync(token))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), token);
                frames.TryWrite($"event: data-changed\ndata: {version}\n\n");
            }
        }
        catch (OperationCanceledException) { }
    }

    // The client reloads its lists before it follows a request, so a chat created a moment ago is
    // already there to open; the request is not held back behind the data-changed delay.
    private async Task PublishNavigationAsync(ChannelWriter<string> frames, CancellationToken token)
    {
        try
        {
            await foreach (var target in navigation.SubscribeAsync(token))
                frames.TryWrite($"event: navigate\ndata: {JsonSerializer.Serialize(target)}\n\n");
        }
        catch (OperationCanceledException) { }
    }

    private async Task PublishRunsAsync(ChannelWriter<string> frames, CancellationToken token)
    {
        try
        {
            Dictionary<ChatRunKey, ChatRunSnapshot>? previous = null;
            await foreach (var snapshot in dispatcher.SubscribeAsync(token))
            {
                var current = snapshot.ToDictionary(run => new ChatRunKey(run.ChatId, run.BranchId));
                ChatRunSnapshotUpdate update;
                if (previous is null)
                {
                    update = new ChatRunSnapshotUpdate(true, snapshot, [], [], []);
                }
                else
                {
                    var changed = new List<ChatRunSnapshot>();
                    var appends = new List<ChatRunStreamingAppend>();
                    var drafts = new List<ChatRunDraftAppend>();
                    var keptChanges = new List<ChatRunKey>();
                    foreach (var (key, run) in current)
                    {
                        if (!previous.TryGetValue(key, out var old)) changed.Add(run);
                        else if (ReferenceEquals(old, run)) continue;
                        else if (comparer.IsStreamingAppend(old, run))
                            appends.Add(new ChatRunStreamingAppend(run.ChatId, run.BranchId, run.Revision,
                                run.StreamingContent[old.StreamingContent.Length..]));
                        // The whole snapshot carries the file changes and the recent messages;
                        // sent for every draft publication it cost the page more than the text did.
                        else if (comparer.IsDraftAppend(old, run))
                        {
                            var baseLength = old.DraftContent?.Length ?? 0;
                            drafts.Add(new ChatRunDraftAppend(run.ChatId, run.BranchId, run.Revision, baseLength,
                                run.DraftContent![baseLength..]));
                        }
                        else
                        {
                            var sent = WithoutSentMessages(old, run);
                            if (sent.WorkspaceChanges is not null && Equals(sent.WorkspaceChanges, old.WorkspaceChanges))
                            {
                                sent = sent with { WorkspaceChanges = null };
                                keptChanges.Add(key);
                            }
                            changed.Add(sent);
                        }
                    }
                    update = new ChatRunSnapshotUpdate(false, changed,
                        previous.Keys.Where(key => !current.ContainsKey(key)).ToArray(), appends, drafts, keptChanges);
                }
                previous = current;
                if (!update.IsFull && update.Runs.Count == 0 && update.Removed.Count == 0 && update.StreamingAppends.Count == 0
                    && update.DraftAppends is not { Count: > 0 }) continue;
                frames.TryWrite($"event: snapshot\ndata: {JsonSerializer.Serialize(update)}\n\n");
            }
        }
        catch (OperationCanceledException) { }
    }

    // A run's snapshot carries the last few messages it wrote, so a client can add them without
    // loading the chat. This stream already sent the ones up to the previous revision, and every
    // snapshot sending them again — tool arguments, file contents and all — was most of what the
    // page had to parse during a run. A client that missed them loads the chat as before.
    private static ChatRunSnapshot WithoutSentMessages(ChatRunSnapshot old, ChatRunSnapshot run)
    {
        if (run.MessageDelta is not { } delta || old.MessageDelta is null) return run;
        var unsent = delta.Appends.Where(append => append.Revision > old.ChatRevision).ToArray();
        if (unsent.Length == delta.Appends.Count) return run;
        return run with { MessageDelta = unsent.Length == 0 ? null : new ChatMessageDelta(unsent) };
    }
}

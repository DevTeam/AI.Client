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
        // One response, two sources. Frames are funnelled through a channel so that only this loop
        // ever writes to the body: two producers writing to one HTTP response would interleave.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var frames = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        var producers = Task.WhenAll(PublishRunsAsync(frames.Writer, stop.Token), PublishDataChangesAsync(frames.Writer, stop.Token))
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
                    update = new ChatRunSnapshotUpdate(true, snapshot, [], []);
                }
                else
                {
                    var changed = new List<ChatRunSnapshot>();
                    var appends = new List<ChatRunStreamingAppend>();
                    foreach (var (key, run) in current)
                    {
                        if (!previous.TryGetValue(key, out var old)) changed.Add(run);
                        else if (ReferenceEquals(old, run)) continue;
                        else if (comparer.IsStreamingAppend(old, run))
                            appends.Add(new ChatRunStreamingAppend(run.ChatId, run.BranchId, run.Revision,
                                run.StreamingContent[old.StreamingContent.Length..]));
                        else changed.Add(run);
                    }
                    update = new ChatRunSnapshotUpdate(false, changed,
                        previous.Keys.Where(key => !current.ContainsKey(key)).ToArray(), appends);
                }
                previous = current;
                if (!update.IsFull && update.Runs.Count == 0 && update.Removed.Count == 0 && update.StreamingAppends.Count == 0) continue;
                frames.TryWrite($"event: snapshot\ndata: {JsonSerializer.Serialize(update)}\n\n");
            }
        }
        catch (OperationCanceledException) { }
    }
}

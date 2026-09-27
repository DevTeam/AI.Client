namespace AI.Server.Hosting;

using Contracts.Runs;

/// <summary>Decides how a run's change can be sent to the UI.</summary>
public interface IRunSnapshotComparer
{
    /// <summary>
    /// True when <paramref name="current"/> differs from <paramref name="old"/> only by more
    /// streamed text, so the UI can be sent the new tail instead of the whole snapshot.
    /// </summary>
    bool IsStreamingAppend(ChatRunSnapshot old, ChatRunSnapshot current);
}

namespace AI.Updates;

using AI.Contracts.Updates;

public interface IUpdateFeed
{
    Task<(UpdateRelease? Release, bool StableAvailable)> FindAsync(string product, string runtime,
        string current, UpdateChannel channel, CancellationToken token);
}

namespace AI.Updates;

using AI.Contracts.Updates;

public interface IUpdateManager : IAsyncDisposable
{
    UpdateState State { get; }
    Task RunAsync(CancellationToken token);
    Task<UpdateState> ExecuteAsync(string operation, UpdatePreferences? preferences, CancellationToken token);
}

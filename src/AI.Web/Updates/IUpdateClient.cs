namespace AI.Web.Updates;

using AI.Contracts.Updates;

public interface IUpdateClient : IAsyncDisposable
{
    UpdateState? State { get; }
    bool IsDesktop { get; }
    bool Reconnecting { get; }
    event Action? Changed;
    Task InitializeAsync();
    Task ExecuteAsync(string operation, UpdatePreferences? preferences = null);
}

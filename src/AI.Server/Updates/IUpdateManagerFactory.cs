namespace AI.Updates;

public interface IUpdateManagerFactory
{
    IUpdateManager Create(string product, string dataDirectory, Func<CancellationToken, Task<bool>> tryEnterMaintenance,
        Action leaveMaintenance, Action shutdown);
}

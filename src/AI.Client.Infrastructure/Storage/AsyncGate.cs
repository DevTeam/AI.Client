namespace AI.Client.Infrastructure.Storage;

// Repositories own their write boundary, including revision checks and the final rename.
internal sealed class AsyncGate : IDisposable
{
    public void Dispose() => _semaphore.Dispose();

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Lease(_semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}

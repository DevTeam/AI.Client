namespace AI.Desktop;

/// <summary>
/// Completes file-system contract calls for the desktop's synchronous callers.
/// </summary>
/// <remarks>
/// The preference stores are read while the window is being built and written while it is closing,
/// and both happen where there is nothing to await from. The call is therefore started on a
/// thread-pool thread instead of on the thread that waits for it: a contract call continues on the
/// context it was begun on, so beginning one on the UI thread and then blocking that thread would
/// deadlock. Blocking here costs what the previous synchronous file reads and writes cost, in the
/// same two moments — the window is starting or closing either way.
/// </remarks>
internal static class SynchronousFiles
{
    public static T Complete<T>(Func<Task<T>> call) => Task.Run(call).GetAwaiter().GetResult();

    public static void Complete(Func<Task> call) => Task.Run(call).GetAwaiter().GetResult();
}

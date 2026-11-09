namespace AI.Infrastructure.Instances;

using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using AI.Contracts.FileSystem;

/// <summary>
/// The instance of a data directory holds <c>&lt;data&gt;/.desktop.lock</c> and listens on a local
/// channel named after the directory: a named pipe on Windows, a Unix domain socket on macOS and
/// Linux, both limited to the current user. A later launch that cannot take the lock connects to
/// the channel instead and asks the running instance to come to the front.
/// </summary>
/// <remarks>
/// The lock, not the channel, decides who is the instance: on Unix a new listener replaces the
/// socket file of an older one, so only the lock holder may listen. The lock and the channel are
/// platform calls on purpose: a cross-process guarantee that no in-process fake can model.
/// </remarks>
public sealed class SingleInstance(IFileSystem files, IForegroundHandoff foregroundHandoff) : ISingleInstance
{
    private const byte ActivateCommand = 1;
    private const byte Accepted = 1;
    private const byte Closing = 0;
    private static readonly TimeSpan ClosingPause = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ConnectAttempt = TimeSpan.FromMilliseconds(500);

    /// <summary>How long a later launch waits for the running instance before starting on its own.</summary>
    public TimeSpan AnswerTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public IInstanceClaim Claim(string dataDirectory)
    {
        var root = Path.GetFullPath(dataDirectory);
        files.CreateDirectoryAsync(root, ownerOnly: false, CancellationToken.None).GetAwaiter().GetResult();
        var lockPath = Path.Combine(root, ".desktop.lock");
        var channel = ChannelName(root);
        var waiting = Stopwatch.StartNew();
        while (true)
        {
            if (TryLock(lockPath) is { } held)
            {
                return new PrimaryInstance(held, channel);
            }

            // Off this thread: the desktop calls this from its UI thread before any UI exists, and
            // nothing awaited here may need to come back to it.
            if (Task.Run(() => TryActivateAsync(channel)).GetAwaiter().GetResult())
            {
                return new ForwardedInstance();
            }

            if (waiting.Elapsed >= AnswerTimeout)
            {
                // An instance that hangs, or an older build without the channel: this launch goes
                // on as before, and the data-directory lock still keeps the two from sharing data.
                Trace.TraceWarning($"Another AI Client window holds '{root}' but did not answer; starting this one anyway.");
                return new UnansweredInstance();
            }

            // The holder may have closed in the meantime: try the lock again.
        }
    }

    /// <summary>
    /// The directory, not its spelling, names the channel. A short name keeps the socket path on
    /// macOS, which lives under a long temporary directory, within its 104-character limit.
    /// </summary>
    private static string ChannelName(string root)
    {
        var key = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (OperatingSystem.IsWindows())
        {
            key = key.ToUpperInvariant();
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return "ai-client-desktop-" + Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    private static FileStream? TryLock(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private async Task<bool> TryActivateAsync(string channel)
    {
        await using var pipe = new NamedPipeClientStream(".", channel, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            using var connecting = new CancellationTokenSource(ConnectAttempt);
            await pipe.ConnectAsync(connecting.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }

        try
        {
            using var answering = new CancellationTokenSource(AnswerTimeout);
            var processId = new byte[sizeof(int)];
            await pipe.ReadExactlyAsync(processId, answering.Token).ConfigureAwait(false);
            // Before the request: once the running instance hears it, it activates its window at once.
            foregroundHandoff.AllowForeground(BinaryPrimitives.ReadInt32LittleEndian(processId));
            await pipe.WriteAsync(new[] { ActivateCommand }, answering.Token).ConfigureAwait(false);
            await pipe.FlushAsync(answering.Token).ConfigureAwait(false);
            var reply = new byte[1];
            await pipe.ReadExactlyAsync(reply, answering.Token).ConfigureAwait(false);
            if (reply[0] == Accepted)
            {
                return true;
            }

            // The running window is closing: give its process a moment to let the lock go.
            await Task.Delay(ClosingPause, answering.Token).ConfigureAwait(false);
            return false;
        }
        catch (Exception error) when (error is OperationCanceledException or IOException)
        {
            return false;
        }
    }

    private sealed class PrimaryInstance : IInstanceClaim
    {
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
        private readonly FileStream _lock;
        private readonly string _channel;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _listening;
        private readonly Lock _sync = new();
        private Func<bool>? _activate;
        private bool _pending;

        public PrimaryInstance(FileStream held, string channel)
        {
            _lock = held;
            _channel = channel;
            _listening = Task.Run(ListenAsync);
        }

        public bool IsForwarded => false;

        public void OnActivation(Func<bool> activate)
        {
            ArgumentNullException.ThrowIfNull(activate);
            bool pending;
            lock (_sync)
            {
                _activate = activate;
                pending = _pending;
                _pending = false;
            }

            if (pending)
            {
                _ = activate();
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            try
            {
                _listening.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // Listening ended with an error already reported; the lock is released below anyway.
            }

            _stop.Dispose();
            _lock.Dispose();
        }

        private async Task ListenAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_channel, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                    await AnswerAsync(server).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
                {
                    // One broken request, or the name is briefly taken: the next launch still gets an answer.
                    Trace.TraceWarning($"The single-instance channel failed: {error.Message}");
                    try
                    {
                        await Task.Delay(RetryDelay, _stop.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        private async Task AnswerAsync(NamedPipeServerStream server)
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            request.CancelAfter(RequestTimeout);
            var processId = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(processId, Environment.ProcessId);
            await server.WriteAsync(processId, request.Token).ConfigureAwait(false);
            await server.FlushAsync(request.Token).ConfigureAwait(false);
            var command = new byte[1];
            await server.ReadExactlyAsync(command, request.Token).ConfigureAwait(false);
            if (command[0] != ActivateCommand)
            {
                return;
            }

            var reply = Activate() ? Accepted : Closing;
            await server.WriteAsync(new[] { reply }, request.Token).ConfigureAwait(false);
            await server.FlushAsync(request.Token).ConfigureAwait(false);
        }

        /// <summary>Before the window exists the request waits for it: the window is about to show anyway.</summary>
        private bool Activate()
        {
            Func<bool>? activate;
            lock (_sync)
            {
                activate = _activate;
                _pending = activate is null;
            }

            return activate?.Invoke() ?? true;
        }
    }

    private sealed class ForwardedInstance : IInstanceClaim
    {
        public bool IsForwarded => true;

        public void OnActivation(Func<bool> activate)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class UnansweredInstance : IInstanceClaim
    {
        public bool IsForwarded => false;

        public void OnActivation(Func<bool> activate)
        {
        }

        public void Dispose()
        {
        }
    }
}

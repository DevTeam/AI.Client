namespace AI.Client.Desktop;

using System.Runtime.InteropServices;

internal sealed class ProcessSignals : IProcessSignals
{
    public IDisposable OnTermination(Action terminate)
    {
        ArgumentNullException.ThrowIfNull(terminate);
        PosixSignal[] signals = [PosixSignal.SIGTERM, PosixSignal.SIGINT, PosixSignal.SIGQUIT];
        return new Registrations(signals
            .Select(signal => PosixSignalRegistration.Create(signal, context =>
            {
                // Cancelled so the runtime does not end the process on the spot; the app shuts
                // down properly instead, which stops the server and releases the data directory.
                context.Cancel = true;
                terminate();
            }))
            .ToArray());
    }

    private sealed class Registrations(PosixSignalRegistration[] registrations) : IDisposable
    {
        public void Dispose()
        {
            foreach (var registration in registrations) registration.Dispose();
        }
    }
}

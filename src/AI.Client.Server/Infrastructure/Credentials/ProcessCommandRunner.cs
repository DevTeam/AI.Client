namespace AI.Client.Infrastructure.Credentials;

using System.ComponentModel;
using System.Diagnostics;

public sealed class ProcessCommandRunner : ICommandRunner
{
    // A keyring that waits on an unlock prompt nobody answers must not hang the request that
    // needed a credential; the caller falls back or reports instead.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public CommandResult? Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var start = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Win32Exception)
        {
            return null;
        }

        if (process is null) return null;
        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (standardInput is not null) process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
            if (!process.WaitForExit(Timeout))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return new CommandResult(-1, string.Empty, $"'{fileName}' did not finish within {Timeout.TotalSeconds:0} s.");
            }

            return new CommandResult(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }
    }
}

namespace Build.Targets;

using System.Diagnostics;

internal sealed class RunTarget(IProcessRunner processRunner, IBuildPaths buildPaths) : IRunTarget
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var output = buildPaths.HostOutputPath;
        // `dotnet publish` copies the default MCP server alongside the host through the existing
        // `PublishDefaultMcpServer` target, so the resulting directory is self-contained and
        // outside `src/`. A running instance therefore holds no handles to files that
        // `dotnet build` or `dotnet test` need to overwrite.
        var publish = await processRunner.RunAsync(
            "Publish host",
            "dotnet",
            ["publish", "src/AI.Client.Host/AI.Client.Host.csproj", "--nologo", "--output", output],
            cancellationToken);
        if (publish != 0)
        {
            return publish;
        }

        var exe = Path.Combine(output, "AI.Client.Host.exe");
        if (!File.Exists(exe))
        {
            await Console.Error.WriteLineAsync($"Host executable was not produced at {exe}.");
            return 1;
        }

        // `process` is assigned inside the `try` and read from the Ctrl+C handler; the handler is
        // installed only after the process exists, but C# captures variables by reference, so the
        // handler sees the assignment that happens inside `try`.
        Process? process = null;
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            if (process is { HasExited: false })
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
        };
        Console.CancelKeyPress += cancel;
        try
        {
            process = Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = output
            });
            if (process is null)
            {
                await Console.Error.WriteLineAsync($"Failed to start {exe}.");
                return 1;
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }
}

namespace Build.Targets;

using System.Diagnostics;

internal sealed class RunBothTarget(IProcessRunner processRunner, IBuildPaths buildPaths) : IRunBothTarget
{
    public async Task<int> RunAsync(RunBothOptions options, CancellationToken cancellationToken)
    {
        // 1. Publish host first. We reuse the existing `ProcessRunner` because it already logs
        //    stdout/stderr to `artifacts/logs/` and surfaces the tail on failure — same behaviour
        //    as `RunTarget` had on its own.
        var hostOutput = buildPaths.HostOutputPath;
        var publish = await processRunner.RunAsync(
            "Publish host",
            "dotnet",
            ["publish", "src/AI.Host/AI.Host.csproj", "--nologo", "--output", hostOutput],
            cancellationToken);
        if (publish != 0)
        {
            return publish;
        }

        var hostExe = Path.Combine(hostOutput, "AI.Host.exe");
        if (!File.Exists(hostExe))
        {
            await Console.Error.WriteLineAsync($"Host executable was not produced at {hostExe}.");
            return 1;
        }

        // 2. Resolve final URLs. The defaults are the same as the legacy `.run.xml` files so an
        //    unchanged developer machine keeps working; CORS is required because the frontend now
        //    lives on a separate origin and the host no longer serves it.
        var hostUrls = string.IsNullOrWhiteSpace(options.HostUrls) ? "http://localhost:52173" : options.HostUrls;
        var webUrls = string.IsNullOrWhiteSpace(options.WebUrls) ? "http://localhost:52174" : options.WebUrls;
        var corsOrigins = string.IsNullOrWhiteSpace(options.CorsOrigins) ? webUrls : options.CorsOrigins;

        Console.WriteLine($"Host: {hostUrls}");
        Console.WriteLine($"Web:  {webUrls}/?api={hostUrls}/");
        Console.WriteLine($"CORS: {corsOrigins}");
        Console.WriteLine();

        // 3. Start both processes. We deliberately do NOT redirect stdout/stderr: Rider's run
        //    window — and any other terminal — already multiplexes a child's output into the
        //    parent's console, which is exactly what we want for local development. Redirecting
        //    here would force us to also implement an `OutputDataReceived` relay, and the relay's
        //    lifetime would have to outlast both processes — error-prone and not useful here.
        var hostStart = new ProcessStartInfo(hostExe)
        {
            UseShellExecute = false,
            WorkingDirectory = hostOutput
        };
        hostStart.ArgumentList.Add("--urls");
        hostStart.ArgumentList.Add(hostUrls);
        if (!string.IsNullOrWhiteSpace(options.Environment))
        {
            hostStart.Environment["ASPNETCORE_ENVIRONMENT"] = options.Environment;
        }
        hostStart.Environment["Cors__AllowedOrigins"] = corsOrigins;

        var webStart = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            WorkingDirectory = buildPaths.SolutionDirectory
        };
        webStart.ArgumentList.Add("run");
        webStart.ArgumentList.Add("--project");
        webStart.ArgumentList.Add("src/AI.Web/AI.Web.csproj");
        webStart.ArgumentList.Add("--no-launch-profile");
        webStart.ArgumentList.Add("--urls");
        webStart.ArgumentList.Add(webUrls);

        using var hostProcess = Process.Start(hostStart)
            ?? throw new InvalidOperationException($"Failed to start {hostExe}.");
        using var webProcess = Process.Start(webStart)
            ?? throw new InvalidOperationException("Failed to start the web dev server.");

        // 4. Ctrl+C kills both trees. The handler is installed only after the processes exist,
        //    but C# captures variables by reference, so it sees the assignments made above.
        //    `TryTerminate` swallows exceptions because by the time we get here the processes
        //    may already be gone.
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            TryTerminate(hostProcess);
            TryTerminate(webProcess);
        };
        Console.CancelKeyPress += cancel;
        try
        {
            // 5. Wait for whichever exits first. We surface the host's exit code as the primary
            //    one — that matches `RunTarget`'s behaviour so users see a non-zero exit when the
            //    host crashes, even if the web dev server is the one still running.
            var winner = await Task.WhenAny(
                hostProcess.WaitForExitAsync(cancellationToken),
                webProcess.WaitForExitAsync(cancellationToken));
            await winner;

            TryTerminate(hostProcess);
            TryTerminate(webProcess);

            return hostProcess.ExitCode;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            TryTerminate(hostProcess);
            TryTerminate(webProcess);
        }
    }

    private static void TryTerminate(Process process)
    {
        if (process.HasExited)
        {
            return;
        }
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Process may have exited between the check and the kill, or we may not own the
            // process tree any more — either way, ignore. The goal is "best effort, no crash".
        }
    }
}

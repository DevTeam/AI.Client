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
        //    With --public-web the pair behaves as an installed Host and the published site: the
        //    Web app's appsettings.Development.json switches to that mode on port 52175, where it
        //    talks to http://127.0.0.1:52173/ with a browser grant, as ai.dev-team.org does.
        var hostUrls = string.IsNullOrWhiteSpace(options.HostUrls)
            ? options.PublicWeb ? "http://127.0.0.1:52173" : "http://localhost:52173"
            : options.HostUrls;
        var webUrls = string.IsNullOrWhiteSpace(options.WebUrls)
            ? options.PublicWeb ? "http://localhost:52175" : "http://localhost:52174"
            : options.WebUrls;
        var corsOrigins = string.IsNullOrWhiteSpace(options.CorsOrigins) ? webUrls : options.CorsOrigins;

        Console.WriteLine($"Host: {hostUrls}{(options.PublicWeb ? " (public Web mode)" : string.Empty)}");
        Console.WriteLine(options.PublicWeb ? $"Web:  {webUrls}/" : $"Web:  {webUrls}/?api={hostUrls}/");
        Console.WriteLine($"CORS: {corsOrigins}");
        Console.WriteLine();

        // 3. Start the Host and wait until it can serve API requests before exposing the Web UI.
        // We deliberately do NOT redirect stdout/stderr: Rider's run
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
        if (options.PublicWeb)
        {
            hostStart.ArgumentList.Add("--public-web");
            hostStart.ArgumentList.Add("--public-origin");
            hostStart.ArgumentList.Add(webUrls.Split(';')[0]);
        }
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
        try
        {
            if (!await WaitForHostAsync(hostProcess, hostUrls, cancellationToken))
                return hostProcess.HasExited && hostProcess.ExitCode != 0 ? hostProcess.ExitCode : 1;

            using var webProcess = Process.Start(webStart)
                ?? throw new InvalidOperationException("Failed to start the web dev server.");

            // 4. Ctrl+C kills both trees. The processes also stop when the build command ends.
            ConsoleCancelEventHandler cancel = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                TryTerminate(hostProcess);
                TryTerminate(webProcess);
            };
            Console.CancelKeyPress += cancel;
            try
            {
                // 5. Wait for whichever exits first. Surface the host's exit code as the primary
                //    one, as `RunTarget` does when the host crashes.
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
                TryTerminate(webProcess);
            }
        }
        finally
        {
            TryTerminate(hostProcess);
        }
    }

    private static async Task<bool> WaitForHostAsync(
        Process hostProcess, string hostUrls, CancellationToken cancellationToken)
    {
        var address = new Uri(hostUrls.Split(';', StringSplitOptions.RemoveEmptyEntries)[0]);
        var healthUrl = new UriBuilder(address)
        {
            Host = address.Host is "*" or "+" or "0.0.0.0" or "::" ? "localhost" : address.Host,
            Path = "api/health"
        }.Uri;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var waiting = Stopwatch.StartNew();
        Console.WriteLine($"Waiting for Host at {healthUrl}...");
        while (waiting.Elapsed < TimeSpan.FromMinutes(2))
        {
            if (hostProcess.HasExited)
            {
                await Console.Error.WriteLineAsync($"Host exited before becoming ready (exit code {hostProcess.ExitCode}).");
                return false;
            }

            try
            {
                using var response = await client.GetAsync(healthUrl, cancellationToken);
                if (response.IsSuccessStatusCode && !hostProcess.HasExited)
                {
                    Console.WriteLine("Host is ready.");
                    return true;
                }
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException
                                          && !cancellationToken.IsCancellationRequested)
            {
                // The Host has not bound its port yet, or this individual probe timed out.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        await Console.Error.WriteLineAsync($"Host did not become ready at {healthUrl} within two minutes.");
        return false;
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

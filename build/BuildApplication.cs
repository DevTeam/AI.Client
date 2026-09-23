namespace Build;

using Targets;
using System.CommandLine;

internal sealed class BuildApplication(
    string[] args,
    IBuildSolutionTarget buildSolutionTarget,
    ITestSolutionTarget testSolutionTarget,
    IVerifyTarget verifyTarget,
    IPublishTarget publishTarget,
    IPublishDesktopTarget publishDesktopTarget,
    IRunTarget runTarget,
    IRunBothTarget runBothTarget,
    CancellationToken cancellationToken)
{
    public Task<int> RunAsync()
    {
        var root = new RootCommand("AI.Client build automation");
        RegisterBuild(root);
        RegisterTest(root);
        RegisterVerify(root);
        RegisterPublish(root);
        RegisterPublishDesktop(root);
        RegisterHost(root);
        return root.Parse(args).InvokeAsync();
    }

    private void RegisterBuild(RootCommand root)
    {
        var command = new Command("build", "Build the AI.Client solution.");
        command.SetAction(_ => buildSolutionTarget.RunAsync(cancellationToken));
        root.Subcommands.Add(command);
    }

    private void RegisterTest(RootCommand root)
    {
        var command = new Command("test", "Run the fast unit test suite.");
        command.SetAction(_ => testSolutionTarget.RunAsync(cancellationToken));
        root.Subcommands.Add(command);
    }

    private void RegisterVerify(RootCommand root)
    {
        var command = new Command("verify", "Build the solution and run the fast unit test suite.");
        command.SetAction(_ => verifyTarget.RunAsync(cancellationToken));
        root.Subcommands.Add(command);
    }

    private void RegisterPublishDesktop(RootCommand root)
    {
        var runtime = new Option<string>("--runtime")
        {
            Description = "Target platform: win-x64, win-arm64, osx-arm64, osx-x64, linux-x64 or linux-arm64.",
            DefaultValueFactory = _ => System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier
        };
        runtime.AcceptOnlyFromAmong("win-x64", "win-arm64", "osx-arm64", "osx-x64", "linux-x64", "linux-arm64");
        var output = new Option<string?>("--output")
        {
            Description = "Directory for the published desktop app. Defaults to artifacts/desktop/<runtime>."
        };
        var command = new Command("publish-desktop", "Publish the self-contained AI.Client desktop app for one platform.");
        command.Options.Add(runtime);
        command.Options.Add(output);
        command.SetAction(parseResult =>
        {
            var target = parseResult.GetValue(runtime)!;
            return publishDesktopTarget.RunAsync(target,
                parseResult.GetValue(output) ?? Path.Combine("artifacts", "desktop", target), cancellationToken);
        });
        root.Subcommands.Add(command);
    }

    private void RegisterPublish(RootCommand root)
    {
        var output = new Option<string>("--output")
        {
            Description = "Directory for the published Host application.",
            DefaultValueFactory = _ => "artifacts/publish"
        };
        var command = new Command("publish", "Publish the local AI.Client Host application.");
        command.Options.Add(output);
        command.SetAction(parseResult => publishTarget.RunAsync(
            parseResult.GetValue(output)!,
            cancellationToken));
        root.Subcommands.Add(command);
    }

    private void RegisterHost(RootCommand root)
    {
        // `host` is the legacy name for the run target. Renamed to `run` everywhere else, but kept
        // here as an alias so existing `.run` files and muscle memory continue to work until they
        // get updated.
        var hostCommand = new Command("host", "Alias for `run`. Publish AI.Client.Host to artifacts/host/ and run it without blocking source-tree builds and tests.");
        hostCommand.SetAction(_ => runTarget.RunAsync(cancellationToken));
        root.Subcommands.Add(hostCommand);

        var runCommand = new Command("run", "Publish AI.Client.Host to artifacts/host/ and run it. The frontend is started separately (see AI.Client Backend.run.xml).");
        runCommand.SetAction(_ => runTarget.RunAsync(cancellationToken));
        root.Subcommands.Add(runCommand);

        // `run-both` publishes the host and then starts both the host process and the web dev
        // server in parallel. The frontend now runs in its own process on a different origin, so
        // this is the one-stop command for local development after the host/frontend split.
        var hostUrls = new Option<string?>("--host-urls")
        {
            Description = "Kestrel URLs forwarded to the host as the --urls argument.",
            DefaultValueFactory = _ => "http://localhost:52173"
        };
        var webUrls = new Option<string?>("--web-urls")
        {
            Description = "URL for the Blazor WASM dev server.",
            DefaultValueFactory = _ => "http://localhost:52174"
        };
        var corsOrigins = new Option<string?>("--cors-origins")
        {
            Description = "Comma-separated origins allowed by CORS. Forwarded as Cors__AllowedOrigins.",
            DefaultValueFactory = _ => null
        };
        var environment = new Option<string?>("--environment")
        {
            Description = "ASPNETCORE_ENVIRONMENT for the host process.",
            DefaultValueFactory = _ => "Development"
        };
        var runBothCommand = new Command(
            "run-both",
            "Publish AI.Client.Host, then run the host and the web dev server in parallel. CORS is configured automatically.");
        runBothCommand.Options.Add(hostUrls);
        runBothCommand.Options.Add(webUrls);
        runBothCommand.Options.Add(corsOrigins);
        runBothCommand.Options.Add(environment);
        runBothCommand.SetAction(parseResult => runBothTarget.RunAsync(
            new RunBothOptions(
                parseResult.GetValue(hostUrls),
                parseResult.GetValue(webUrls),
                parseResult.GetValue(corsOrigins),
                parseResult.GetValue(environment)),
            cancellationToken));
        root.Subcommands.Add(runBothCommand);
    }
}

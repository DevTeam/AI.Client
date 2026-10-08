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
    IPublishWebTarget publishWebTarget,
    IPackageReleaseTarget packageReleaseTarget,
    IRunTarget runTarget,
    IRunBothTarget runBothTarget,
    IReadmeTarget readmeTarget,
    IPrepareTextCorrectionTarget prepareTextCorrectionTarget,
    CancellationToken cancellationToken)
{
    public Task<int> RunAsync()
    {
        var root = new RootCommand("AI build automation");
        RegisterBuild(root);
        RegisterTest(root);
        RegisterTestAll(root);
        RegisterVerify(root);
        RegisterPublish(root);
        RegisterPublishDesktop(root);
        RegisterPublishWeb(root);
        RegisterPackageRelease(root);
        RegisterHost(root);
        RegisterReadme(root);
        RegisterTextCorrection(root);
        return root.Parse(args).InvokeAsync();
    }

    private void RegisterBuild(RootCommand root)
    {
        var command = new Command("build", "Build the AI solution.");
        command.SetAction(_ => buildSolutionTarget.RunAsync(cancellationToken));
        root.Subcommands.Add(command);
    }

    private void RegisterReadme(RootCommand root)
    {
        var command = new Command("readme", "Generate README.md from its Razor template.");
        command.SetAction(_ => readmeTarget.RunAsync(cancellationToken));
        root.Subcommands.Add(command);
    }

    private void RegisterTextCorrection(RootCommand root)
    {
        var command = new Command("prepare-text-correction", "Compile dictionary resources for layout correction.");
        command.SetAction(_ => prepareTextCorrectionTarget.RunAsync(cancellationToken));
        root.Subcommands.Add(command);
    }

    private void RegisterTest(RootCommand root)
    {
        var command = new Command("test", "Run the fast unit test suite.");
        command.SetAction(_ => testSolutionTarget.RunAsync(cancellationToken));
        root.Subcommands.Add(command);
    }

    private void RegisterTestAll(RootCommand root)
    {
        var command = new Command("test-all", "Run unit and integration tests.");
        command.SetAction(_ => testSolutionTarget.RunAllAsync(cancellationToken));
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
        var command = new Command("publish-desktop", "Publish the self-contained AI desktop app for one platform.");
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

    private void RegisterPublishWeb(RootCommand root)
    {
        var command = new Command("publish-web", "Prepare the public AI Web application for GitHub Pages.");
        command.SetAction(_ => publishWebTarget.RunAsync(cancellationToken));
        root.Subcommands.Add(command);
    }

    private void RegisterPackageRelease(RootCommand root)
    {
        var runtime = new Option<string>("--runtime") { Required = true };
        runtime.AcceptOnlyFromAmong("win-x64", "win-arm64", "osx-x64", "osx-arm64", "linux-x64", "linux-arm64");
        var version = new Option<string>("--version") { Required = true };
        var command = new Command("package-release", "Publish self-contained Host and Desktop installers for one runtime.");
        command.Options.Add(runtime);
        command.Options.Add(version);
        command.SetAction(parse => packageReleaseTarget.RunAsync(
            parse.GetValue(runtime)!, parse.GetValue(version)!, cancellationToken));
        root.Subcommands.Add(command);
    }

    private void RegisterPublish(RootCommand root)
    {
        var output = new Option<string>("--output")
        {
            Description = "Directory for the published Host application.",
            DefaultValueFactory = _ => "artifacts/publish"
        };
        var command = new Command("publish", "Publish the local AI Host application.");
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
        var hostCommand = new Command("host", "Alias for `run`. Publish AI.Host to artifacts/host/ and run it without blocking source-tree builds and tests.");
        hostCommand.SetAction(_ => runTarget.RunAsync(cancellationToken));
        root.Subcommands.Add(hostCommand);

        var runCommand = new Command("run", "Publish AI.Host to artifacts/host/ and run it. The frontend is started separately (see AI Backend.run.xml).");
        runCommand.SetAction(_ => runTarget.RunAsync(cancellationToken));
        root.Subcommands.Add(runCommand);

        // `run-both` publishes the host and then starts both the host process and the web dev
        // server in parallel. The frontend now runs in its own process on a different origin, so
        // this is the one-stop command for local development after the host/frontend split.
        var hostUrls = new Option<string?>("--host-urls")
        {
            Description = "Kestrel URLs forwarded to the host as the --urls argument.",
            DefaultValueFactory = _ => null
        };
        var webUrls = new Option<string?>("--web-urls")
        {
            Description = "URL for the Blazor WASM dev server.",
            DefaultValueFactory = _ => null
        };
        var corsOrigins = new Option<string?>("--cors-origins")
        {
            Description = "Comma-separated origins allowed by CORS. Forwarded as Cors__AllowedOrigins.",
            DefaultValueFactory = _ => null
        };
        var publicWeb = new Option<bool>("--public-web")
        {
            Description = "Run the Host as the installed one (--public-web) and the Web app as the published one, on http://localhost:52175, to debug browser pairing."
        };
        var environment = new Option<string?>("--environment")
        {
            Description = "ASPNETCORE_ENVIRONMENT for the host process.",
            DefaultValueFactory = _ => "Development"
        };
        var runBothCommand = new Command(
            "run-both",
            "Publish AI.Host, then run the host and the web dev server in parallel. CORS is configured automatically.");
        runBothCommand.Options.Add(hostUrls);
        runBothCommand.Options.Add(webUrls);
        runBothCommand.Options.Add(corsOrigins);
        runBothCommand.Options.Add(environment);
        runBothCommand.Options.Add(publicWeb);
        runBothCommand.SetAction(parseResult => runBothTarget.RunAsync(
            new RunBothOptions(
                parseResult.GetValue(hostUrls),
                parseResult.GetValue(webUrls),
                parseResult.GetValue(corsOrigins),
                parseResult.GetValue(environment),
                parseResult.GetValue(publicWeb)),
            cancellationToken));
        root.Subcommands.Add(runBothCommand);
    }
}

namespace Build;

using Targets;
using System.CommandLine;

internal sealed class BuildApplication(
    string[] args,
    IBuildSolutionTarget buildSolutionTarget,
    ITestSolutionTarget testSolutionTarget,
    IVerifyTarget verifyTarget,
    IPublishTarget publishTarget,
    IChatSessionTarget chatSessionTarget,
    IHostTarget hostTarget,
    CancellationToken cancellationToken)
{
    public Task<int> RunAsync()
    {
        var root = new RootCommand("AI.Client build automation");
        RegisterBuild(root);
        RegisterTest(root);
        RegisterVerify(root);
        RegisterPublish(root);
        RegisterChat(root);
        RegisterHost(root);
        return root.Parse(args).InvokeAsync();
    }

    private void RegisterChat(RootCommand root)
    {
        var arguments = new Argument<string[]>("arguments") { Arity = ArgumentArity.ZeroOrMore };
        var command = new Command("chat", "Run the headless chat session CLI.");
        command.Arguments.Add(arguments);
        command.SetAction(parseResult => chatSessionTarget.RunAsync(parseResult.GetValue(arguments) ?? [], cancellationToken));
        root.Subcommands.Add(command);
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
        // Publishes AI.Client.Host to `artifacts/host/` and launches it from there. The output
        // directory lives outside `src/`, so the running host never holds handles to files that
        // `dotnet build` or `dotnet test` need to overwrite.
        var command = new Command("host", "Publish AI.Client.Host to artifacts/host/ and run it without blocking source-tree builds and tests.");
        command.SetAction(_ => hostTarget.RunAsync(cancellationToken));
        root.Subcommands.Add(command);
    }
}

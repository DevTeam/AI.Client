namespace Build.Targets;

internal sealed class ChatSessionTarget(IProcessRunner processRunner) : IChatSessionTarget
{
    public Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        processRunner.RunAsync("Headless chat session", "dotnet",
            ["run", "--project", "src/AI.Client.Cli", "--", .. arguments], cancellationToken);
}

using AI.Client.Cli;

var composition = new Composition(args);
return await composition.Root.RunAsync();

internal partial class Program(IHeadlessApplication app)
{
    private async Task<int> RunAsync() => await app.RunAsync();
}
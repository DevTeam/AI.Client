using AI.Search.Benchmarks;
using BenchmarkDotNet.Configs;

// `compare` times AI.Search and ripgrep on the same generated corpus and prints one table; anything
// else is passed to BenchmarkDotNet, which runs the in-process benchmarks.
if (args.Length > 0 && args[0] == ComparisonCommand.Name)
{
    return await ComparisonCommand.RunAsync(args).ConfigureAwait(false);
}

// Results go under the build output, which is ignored, rather than into the repository root.
var config = DefaultConfig.Instance
    .WithArtifactsPath(Path.Combine(AppContext.BaseDirectory, "benchmarks"));

BenchmarkDotNet.Running.BenchmarkSwitcher.FromAssembly(typeof(EngineBenchmarks).Assembly).Run(args, config);
return 0;
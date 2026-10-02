using AI.TextCorrection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace AI.TextCorrection.Benchmarks;

/// <summary>Each measured operation prepares a fresh service graph, without cached dictionaries.</summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 5, invocationCount: 1)]
public class PreparationBenchmarks
{
    private string[] _layouts = [];

    [Params("en,ru", "en,ru,fr,es")]
    public string Languages { get; set; } = "en,ru";

    [GlobalSetup]
    public void Setup() => _layouts = Languages.Split(',');

    [Benchmark]
    public async Task<bool> Prepare()
    {
        var composition = new TextCorrectionComposition();
        var preparation = composition.Preparation;
        await preparation.PrepareAsync(_layouts);
        return preparation.IsReady(_layouts);
    }
}

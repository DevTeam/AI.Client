using AI.TextCorrection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace AI.TextCorrection.Benchmarks;

/// <summary>One first request per process; includes preparation and analysis, excludes process startup.</summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.ColdStart, launchCount: 3, warmupCount: 0, iterationCount: 1, invocationCount: 1)]
public class FirstCorrectionBenchmarks
{
    private string[] _layouts = [];

    [Params("en,ru", "en,ru,fr,es")]
    public string Languages { get; set; } = "en,ru";

    [GlobalSetup]
    public void Setup() => _layouts = Languages.Split(',');

    [Benchmark]
    public async Task<IReadOnlyList<TextReplacement>> FirstRequest()
    {
        var composition = new TextCorrectionComposition();
        await composition.Preparation.PrepareAsync(_layouts);
        return composition.Analyzer.Analyze("gjqltim cj vyj d rbyj", _layouts);
    }
}

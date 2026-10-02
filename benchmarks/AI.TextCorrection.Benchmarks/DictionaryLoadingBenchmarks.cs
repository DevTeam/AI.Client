using AI.TextCorrection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace AI.TextCorrection.Benchmarks;

/// <summary>Separates Hunspell parsing from building the plausibility model.</summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 5, invocationCount: 1)]
public class DictionaryLoadingBenchmarks
{
    private ITextDictionaries _dictionaries = null!;
    private WordLexicon _vocabulary = null!;

    [Params("en", "ru", "fr", "es")]
    public string Language { get; set; } = "en";

    [GlobalSetup]
    public void Setup()
    {
        _dictionaries = new EmbeddedTextDictionaries();
        _vocabulary = new WordLexicon();
    }

    [Benchmark]
    public Task LoadHunspell() => new HunspellWordLexicon(_vocabulary, _dictionaries).PrepareAsync(Language);

    [Benchmark]
    public Task BuildTrigrams() => new DictionaryWordPlausibility(_dictionaries).PrepareAsync(Language);
}

using AI.TextCorrection;
using BenchmarkDotNet.Attributes;

namespace AI.TextCorrection.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class SpellingCorrectionBenchmarks
{
    private ITextAutoCorrectionAnalyzer _analyzer = null!;
    private HunspellWordLexicon _lexicon = null!;
    private string[] _layouts = [];
    private string _text = "";
    private string _word = "";

    [Params("en", "ru", "en,ru")]
    public string Languages { get; set; } = "en";

    [GlobalSetup]
    public async Task Setup()
    {
        _layouts = Languages.Split(',');
        _text = Languages switch
        {
            "en" => "helllo dictioanry",
            "ru" => "првиет правопсиание",
            _ => "helllo ghbdtn првиет"
        };
        _word = Languages == "ru" ? "првиет" : "helllo";
        var composition = new TextCorrectionComposition();
        await composition.Preparation.PrepareAsync(_layouts);
        _analyzer = composition.AutoCorrection;
        _ = _analyzer.Analyze(_text, _layouts);
        _lexicon = new HunspellWordLexicon(new WordLexicon(), new EmbeddedTextDictionaries());
        await _lexicon.PrepareAsync(_layouts[0]);
        _ = _lexicon.Suggest(_layouts[0], _word);
    }

    [Benchmark]
    public IReadOnlyList<TextReplacement> WarmAutomaticCorrection() => _analyzer.Analyze(_text, _layouts);

    [Benchmark]
    public IReadOnlyList<string> UncachedSpellingSuggestions() => _lexicon.Suggest(_layouts[0], _word);
}

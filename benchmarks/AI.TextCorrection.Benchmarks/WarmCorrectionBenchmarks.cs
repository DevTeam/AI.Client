using AI.TextCorrection;
using BenchmarkDotNet.Attributes;

namespace AI.TextCorrection.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class WarmCorrectionBenchmarks
{
    private ITextCorrectionAnalyzer _analyzer = null!;
    private string[] _layouts = [];
    private string _text = "";

    [Params("en,ru", "en,ru,fr,es")]
    public string Languages { get; set; } = "en,ru";

    [Params(CorrectionScenario.RussianPhrase, CorrectionScenario.EnglishPhrase,
        CorrectionScenario.CorrectText, CorrectionScenario.UnknownWords,
        CorrectionScenario.FrenchSpanish, CorrectionScenario.LongProtectedText)]
    public CorrectionScenario Scenario { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _layouts = Languages.Split(',');
        _text = Scenario switch
        {
            CorrectionScenario.RussianPhrase => "gjqltim cj vyj d rbyj",
            CorrectionScenario.EnglishPhrase => "рщц фку нщг?",
            CorrectionScenario.CorrectText => "Привет! Что написать тебе ещё? Hello, how are you?",
            CorrectionScenario.UnknownWords => "gjqltim abcdefgh xyzabcdef d rbyj",
            CorrectionScenario.FrenchSpanish => "fq;ille 2cole ma;ana espa;ol",
            CorrectionScenario.LongProtectedText => string.Concat(Enumerable.Repeat(
                "gjqltim cj vyj d rbyj https://example.com/gjqltim `gjqltim`\n```cs\nvar text = \"rbyj\";\n```\n", 60)),
            _ => throw new InvalidOperationException("Unknown benchmark scenario.")
        };
        if (_text.Length > 8192) throw new InvalidOperationException("Input exceeds the analyzer limit.");
        var composition = new TextCorrectionComposition();
        await composition.Preparation.PrepareAsync(_layouts);
        _analyzer = composition.Analyzer;
        _ = _analyzer.Analyze(_text, _layouts);
    }

    [Benchmark]
    public IReadOnlyList<TextReplacement> Analyze() => _analyzer.Analyze(_text, _layouts);
}

public enum CorrectionScenario
{
    RussianPhrase,
    EnglishPhrase,
    CorrectText,
    UnknownWords,
    FrenchSpanish,
    LongProtectedText
}

// DI guide: [Pure.DI conventions](../../docs/30-dependency-injection.md).
namespace AI.TextCorrection.Configuration;

using System.Diagnostics;
using Pure.DI;
using Resources;

/// <summary>Shared registrations linked by consumers and reused through DependsOn.</summary>
internal sealed class TextCorrectionComposition
{
    [Conditional("DI")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "The composition uses instance methods by design.")]
    private void Setup() => DI.Setup(kind: CompositionKind.Internal)
        .Hint(Hint.Comments, "Off")
        // Dictionary/model caches and readiness must be shared by analysis and preparation.
        .Singleton<KeyboardLayouts, EmbeddedTextDictionaries, DictionaryWordPlausibility, HunspellWordLexicon,
            TextCorrectionPreparation>()
        .Transient<WordLexicon>("application")
        .Transient<LayoutConverter, WordBoundaries, TextTokenizer, SupportedCorrectionLayouts, TrigramIndexFormat,
            SpellingCorrection, TextAutoCorrectionAnalyzer, TextCorrectionAnalyzer>();
}

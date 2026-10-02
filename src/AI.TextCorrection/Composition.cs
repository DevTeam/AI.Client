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
        .Bind<IKeyboardLayouts>().As(Lifetime.Singleton).To<KeyboardLayouts>()
        .Bind<ILayoutConverter>().As(Lifetime.Singleton).To<LayoutConverter>()
        .Bind<IWordBoundaries>().As(Lifetime.Singleton).To<WordBoundaries>()
        .Bind<ITextTokenizer>().As(Lifetime.Singleton).To<TextTokenizer>()
        .Bind<ITextDictionaries>().As(Lifetime.Singleton).To<EmbeddedTextDictionaries>()
        .Bind<ISupportedCorrectionLayouts>().As(Lifetime.Singleton).To<SupportedCorrectionLayouts>()
        .Bind<ITrigramIndexFormat>().As(Lifetime.Singleton).To<TrigramIndexFormat>()
        .Bind<IWordPlausibility, IWordPlausibilityPreparation>().As(Lifetime.Singleton).To<DictionaryWordPlausibility>()
        .Bind<WordLexicon>().As(Lifetime.Singleton).To<WordLexicon>()
        .Bind<IWordLexicon, IWordLexiconPreparation, IWordSuggestions>().As(Lifetime.Singleton).To<HunspellWordLexicon>()
        .Bind<ISpellingCorrection>().As(Lifetime.Singleton).To<SpellingCorrection>()
        .Bind<ITextAutoCorrectionAnalyzer>().As(Lifetime.Singleton).To<TextAutoCorrectionAnalyzer>()
        .Bind<ITextCorrectionPreparation>().As(Lifetime.Singleton).To<TextCorrectionPreparation>()
        .Bind<ITextCorrectionAnalyzer>().As(Lifetime.Singleton).To<TextCorrectionAnalyzer>();
}

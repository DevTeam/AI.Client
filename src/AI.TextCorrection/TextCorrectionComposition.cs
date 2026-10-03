// DI guide: [Pure.DI conventions](../../docs/30-dependency-injection.md).
namespace AI.TextCorrection;

using System.Diagnostics;
using Pure.DI;

/// <summary>Standalone roots built from the same registrations used by Build and Web.</summary>
public sealed partial class TextCorrectionComposition
{
    [Conditional("DI")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Pure.DI composition uses instance methods.")]
    private void Setup() => DI.Setup()
        .Hint(Hint.Comments, "Off")
        .Hint(Hint.Resolve, "Off")
        .DependsOn("AI.TextCorrection.Configuration.TextCorrectionComposition")
        .Root<ITextCorrectionAnalyzer>(nameof(Analyzer))
        .Root<ITextAutoCorrectionAnalyzer>(nameof(AutoCorrection))
        .Root<IKeyboardLayouts>(nameof(Layouts))
        .Root<ISupportedCorrectionLayouts>(nameof(SupportedLayouts))
        .Root<IWordBoundaries>(nameof(Boundaries))
        .Root<ITextCorrectionPreparation>(nameof(Preparation));
}

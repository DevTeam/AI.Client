namespace AI.TextCorrection;

using System.Diagnostics;
using Pure.DI;

/// <summary>Standalone roots built from the same registrations used by Build and Web.</summary>
public sealed partial class TextCorrectionComposition
{
    [Conditional("DI")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Pure.DI composition uses instance methods.")]
    private void Setup() => DI.Setup()
        .DependsOn("AI.TextCorrection.Configuration.TextCorrectionComposition")
        .Root<ITextCorrectionAnalyzer>("Analyzer")
        .Root<ITextAutoCorrectionAnalyzer>("AutoCorrection")
        .Root<IKeyboardLayouts>("Layouts")
        .Root<ISupportedCorrectionLayouts>("SupportedLayouts")
        .Root<IWordBoundaries>("Boundaries")
        .Root<ITextCorrectionPreparation>("Preparation");
}

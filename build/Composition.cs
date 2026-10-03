// DI guide: [Pure.DI conventions](../docs/30-dependency-injection.md).
// ReSharper disable UnusedMember.Local
namespace Build;

using Targets;
using Pure.DI;
using System.Diagnostics;

internal sealed partial class Composition
{
    [Conditional("DI")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Pure.DI composition uses instance methods.")]
    private void Setup() =>
        DI.Setup()
            .DependsOn("AI.TextCorrection.Configuration.TextCorrectionComposition")
            .Hint(Hint.Comments, "Off")
            .Hint(Hint.Resolve, "Off")
            .Hint(Hint.ThreadSafe, "Off")
            .Root<BuildApplication>(nameof(Root))
            .Arg<string[]>("args")
            .Arg<CancellationToken>("cancellationToken")
            .Transient<PrepareTextCorrectionTarget, BuildPaths, ProcessRunner, BuildSolutionTarget, TestSolutionTarget,
                VerifyTarget, PublishTarget, PublishDesktopTarget, PublishWebTarget, PackageReleaseTarget, RunTarget,
                RunBothTarget, RazorTemplateEngine, ReadmeTarget>();
}

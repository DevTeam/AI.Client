using Build.Targets;
using Pure.DI;
using System.Diagnostics;

namespace Build;

internal sealed partial class Composition
{
    [Conditional("DI")]
    private static void SetupDI() =>
        DI.Setup()
            .Hint(Hint.Resolve, "Off")
            .Hint(Hint.ThreadSafe, "Off")
            .Root<BuildApplication>(nameof(Root))
            .Arg<string[]>("args")
            .Arg<CancellationToken>("cancellationToken")
            .Singleton<BuildPaths, ProcessRunner, BuildSolutionTarget, TestSolutionTarget, VerifyTarget, PublishTarget, ChatSessionTarget>();
}

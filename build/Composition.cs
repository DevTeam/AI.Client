// ReSharper disable UnusedMember.Local
namespace Build;

using Targets;
using Pure.DI;
using System.Diagnostics;

internal sealed partial class Composition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .Hint(Hint.Resolve, "Off")
            .Hint(Hint.ThreadSafe, "Off")
            .Root<BuildApplication>(nameof(Root))
            .Arg<string[]>("args")
            .Arg<CancellationToken>("cancellationToken")
            .Singleton<BuildPaths, ProcessRunner, BuildSolutionTarget, TestSolutionTarget, VerifyTarget, PublishTarget, ChatSessionTarget>();
}

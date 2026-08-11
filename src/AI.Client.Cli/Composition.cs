using Pure.DI;
using System.Diagnostics;

namespace AI.Client.Cli;

internal sealed partial class Composition
{
    [Conditional("DI")]
    private static void SetupDI() =>
        DI.Setup()
            .Hint(Hint.Resolve, "Off")
            .Hint(Hint.ThreadSafe, "Off")
            .Root<IHeadlessApplication>("Root")
            .Arg<string[]>("args")
            .Singleton<HeadlessPaths, HeadlessApplication, HeadlessSessionStore, HeadlessChatClient>();
}

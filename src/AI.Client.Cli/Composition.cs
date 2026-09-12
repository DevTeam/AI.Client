// ReSharper disable UnusedMember.Local
namespace AI.Client.Cli;

using Pure.DI;
using System.Diagnostics;

internal sealed partial class Composition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .Hint(Hint.Resolve, "Off")
            .Hint(Hint.ThreadSafe, "Off")
            .Root<Program>("Root")
            .Arg<string[]>("args")
            .Singleton(_ => new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
            .Singleton<HeadlessPaths, HeadlessApplication, HeadlessSessionStore, HeadlessChatClient>();
}

// ReSharper disable UnusedMember.Local
namespace AI.Infrastructure.Tests.Tools;

using System.Diagnostics;
using AI.Application.Runs;
using AI.Application.Settings;
using Pure.DI;

/// <summary>
/// The shipped server graph as <see cref="AppToolTests"/> drives it: questions are answered by the
/// test instead of a live run, everything else — the app tools, their MCP host and transport — is
/// what the Host ships.
/// </summary>
internal sealed partial class AppToolsComposition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .DependsOn("AI.Contracts.Composition")
            .DependsOn("AI.Server.Composition")
            .DependsOn("AI.Server.Tests.TestServer")
            .Arg<IUserPromptBroker>("broker")
            // The server already roots its services; this is the extra one the tests reach into.
            .Root<IGlobalSettingsRepository>();
}

// ReSharper disable UnusedMember.Local
namespace AI.Client.Infrastructure.Tests.Tools;

using System.Diagnostics;
using AI.Client.Application.Runs;
using AI.Client.Application.Settings;
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
            .DependsOn("AI.Client.Contracts.Composition")
            .DependsOn("AI.Client.Server.Composition")
            .DependsOn("AI.Client.Server.Tests.TestServer")
            .Arg<IUserPromptBroker>("broker")
            // The server already roots its services; this is the extra one the tests reach into.
            .Root<IGlobalSettingsRepository>();
}

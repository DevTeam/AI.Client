// ReSharper disable UnusedMember.Local
namespace AI.Infrastructure.Tests.Tools;

using System.Diagnostics;
using AI.Application.Settings;
using AI.Application.Tools;
using AI.Infrastructure.Tools;
using Pure.DI;

/// <summary>
/// The shipped server graph as <see cref="AppSubtaskToolTests"/> drives it: the agent a subtask
/// runs on sees the test's tool servers — none, so no child process starts — and the tests call
/// the app server directly through its own connection.
/// </summary>
internal sealed partial class SubtaskComposition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .DependsOn("AI.Contracts.Composition")
            .DependsOn("AI.Server.Composition")
            .DependsOn("AI.Server.Tests.TestServer")
            .Arg<IToolSessionFactory>("tools")
            // The server already roots its services; these are the extra ones the tests reach into.
            .Root<IGlobalSettingsRepository>()
            .Root<AppToolSessionFactory>();
}

// DI guide: [Pure.DI conventions](../../../../docs/30-dependency-injection.md).
// ReSharper disable UnusedMember.Local
namespace AI.Infrastructure.Tests.Tools;

using System.Diagnostics;
using AI.Application.Runs;
using AI.Application.Settings;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Instructions;
using AI.Application.Notifications;
using AI.Application.Skills;
using AI.Application.Tools;
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
            .Hint(Hint.Comments, "Off")
            .Hint(Hint.Resolve, "Off")
            .Root<IGlobalSettingsRepository>(nameof(Settings))
            .Root<IGlobalSecretStore>(nameof(Secrets))
            .Root<IProjectService>(nameof(Projects))
            .Root<IChatService>(nameof(Chats))
            .Root<IStandingInstructions>(nameof(Standing))
            .Root<IGlobalSettingsService>(nameof(GlobalSettings))
            .Root<IToolSessionFactory>(nameof(Sessions))
            .Root<IAppDataChangeSignal>(nameof(Changes))
            .Root<IAppNavigationSignal>(nameof(Navigation))
            .Arg<IUserPromptBroker>("broker")
            .Arg<IConnectionModelsResolver>("modelsResolver");
}

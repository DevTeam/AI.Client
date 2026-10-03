// DI guide: [Pure.DI conventions](../../../../docs/30-dependency-injection.md).
// ReSharper disable UnusedMember.Local
namespace AI.Infrastructure.Tests.Tools;

using System.Diagnostics;
using AI.Application.Settings;
using AI.Application.Tools;
using AI.Infrastructure.Tools;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Runs;
using AI.Application.Instructions;
using AI.Application.Notifications;
using AI.Application.Skills;
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
            .Hint(Hint.Comments, "Off")
            .Hint(Hint.Resolve, "Off")
            .Root<IGlobalSettingsRepository>(nameof(Settings))
            .Root<IProjectService>(nameof(Projects))
            .Root<IChatService>(nameof(Chats))
            .Root<ISkillCatalog>(nameof(Skills))
            .Root<IMcpServerConnection>(nameof(Sessions), typeof(AppToolSessionFactory))
            .Arg<IToolSessionFactory>("tools");
}

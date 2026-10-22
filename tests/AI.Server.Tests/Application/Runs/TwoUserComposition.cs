// DI guide: [Pure.DI conventions](../../../../docs/30-dependency-injection.md).
// ReSharper disable UnusedMember.Local
namespace AI.Application.Tests.Runs;

using System.Diagnostics;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Runs;
using AI.Application.Settings;
using AI.Application.Tools;
using AI.Application.Users;
using AI.Application.Workspace;
using Pure.DI;

/// <summary>
/// The shipped server graph with the real ambient user context instead of the test server's fixed
/// local user, so a test can act for two users in turn.
/// </summary>
internal sealed partial class TwoUserComposition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .DependsOn("AI.Contracts.Composition")
            .DependsOn("AI.Server.Composition")
            .DependsOn("AI.Server.Tests.TestServer")
            .Hint(Hint.Comments, "Off")
            .Hint(Hint.Resolve, "Off")
#pragma warning disable DIW000 // These bindings intentionally replace the test server's and the production edges.
            // The scope a test enters is the one every service reads.
            .Bind<IUserContext>().To((IUserScope scope) => (IUserContext)scope)
            .Arg<IToolSessionFactory>("tools")
            .Arg<IWorkspaceChangeTracker>("workspace")
#pragma warning restore DIW000
            .Root<IUserScope>(nameof(Users))
            .Root<IGlobalSettingsRepository>(nameof(Settings))
            .Root<IProjectService>(nameof(Projects))
            .Root<IChatService>(nameof(Chats))
            .Root<IChatRunDispatcher>(nameof(Dispatcher));
}

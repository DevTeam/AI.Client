// DI guide: [Pure.DI conventions](../../../../docs/30-dependency-injection.md).
// ReSharper disable UnusedMember.Local
namespace AI.Infrastructure.Tests.Storage;

using System.Diagnostics;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Runs;
using AI.Application.Settings;
using AI.Application.Tools;
using AI.Application.Workspace;
using AI.Contracts.Tools;
using AI.Application.Instructions;
using AI.Application.Notifications;
using AI.Application.Skills;
using Pure.DI;

/// <summary>
/// A running server as <see cref="ChatExecutionTests"/> drives it: the tools the agent sees and
/// the workspace tracker are the test's, everything else is the shipped graph.
/// </summary>
internal sealed partial class ChatExecutionComposition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .DependsOn("AI.Contracts.Composition")
            .DependsOn("AI.Server.Composition")
            .DependsOn("AI.Server.Tests.TestServer")
            .Hint(Hint.Comments, "Off")
            .Hint(Hint.Resolve, "Off")
            .Root<IChatRepository>(nameof(ChatRepository))
            .Root<IUserPromptBroker>(nameof(Broker))
            .Root<IChatContextBuilder>(nameof(Context))
            .Root<IToolResultCodec>(nameof(Codec))
            .Root<IClock>(nameof(Clock))
            .Root<IGlobalSettingsRepository>(nameof(Settings))
            .Root<IChatService>(nameof(Chats))
            .Root<IChatRunDispatcher>(nameof(Dispatcher))
            .Root<IStandingInstructions>(nameof(Standing))
            .Root<IProjectService>(nameof(Projects))
            .Root<IChatReplySuggestions>(nameof(ReplySuggestions))
            .Arg<IToolSessionFactory>("tools")
            .Arg<IWorkspaceChangeTracker>("workspace")
            .Transient<ExtensionChatKindPolicy>(Tag.Unique);
}

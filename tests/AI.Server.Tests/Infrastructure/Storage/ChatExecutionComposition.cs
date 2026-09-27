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
            .Arg<IToolSessionFactory>("tools")
            .Arg<IWorkspaceChangeTracker>("workspace")
            // The server already roots its services; these are the extra ones the tests reach into.
            .Root<IChatRepository>()
            .Root<IUserPromptBroker>()
            .Root<IChatContextBuilder>()
            .Root<IToolResultCodec>()
            .Root<IClock>()
            .Root<IGlobalSettingsRepository>();
}

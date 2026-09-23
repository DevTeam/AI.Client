// ReSharper disable UnusedMember.Local
namespace AI.Client.Infrastructure.Tests.Storage;

using System.Diagnostics;
using AI.Client.Application.Chats;
using AI.Client.Application.Projects;
using AI.Client.Application.Runs;
using AI.Client.Application.Settings;
using AI.Client.Application.Tools;
using AI.Client.Application.Workspace;
using AI.Client.Contracts.Tools;
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
            .DependsOn("AI.Client.Contracts.Composition")
            .DependsOn("AI.Client.Server.Composition")
            .DependsOn("AI.Client.Server.Tests.TestServer")
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

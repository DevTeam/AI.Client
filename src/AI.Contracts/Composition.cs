// ReSharper disable UnusedMember.Local
namespace AI.Contracts;

using System.Diagnostics;
using Pure.DI;
using Settings;
using Tools;
using Workspace;

/// <summary>
/// The presentation and codec services both sides of the Host-Web contract share. Nothing is
/// generated here: every consumer links this file into its own project and builds on it with
/// <c>DependsOn("AI.Contracts.Composition")</c>, so Host and Web describe tool calls with
/// the same adapter set instead of two hand-kept copies that drift apart.
/// </summary>
internal sealed class Composition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup(kind: CompositionKind.Internal)
            .Bind<IToolPresentations>().As(Lifetime.Singleton).To((
                GenericToolPresentationAdapter genericAdapter,
                FileToolPresentationAdapter fileAdapter,
                ProcessToolPresentationAdapter processAdapter,
                WebToolPresentationAdapter webAdapter,
                AppReadPresentationAdapter readAdapter,
                AppWritePresentationAdapter writeAdapter,
                AppSubtaskPresentationAdapter subtaskAdapter,
                AskUserPresentationAdapter askAdapter)
                => new ToolPresentations(genericAdapter,
                    [fileAdapter, processAdapter, webAdapter, readAdapter, writeAdapter, subtaskAdapter, askAdapter]))
            .Singleton<GenericToolPresentationAdapter, ToolResultModelProjector, ToolResultCodec,
                ConnectionContextLimitsResolver, UnifiedDiff>();
}

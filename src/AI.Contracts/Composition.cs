// DI guide: [Pure.DI conventions](../../docs/30-dependency-injection.md).
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
            .Hint(Hint.Comments, "Off")
            .Bind<IGenericToolPresentationAdapter>().To<GenericToolPresentationAdapter>()
            // The adapter interface is inherited from a base class; bind that contract explicitly.
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<FileToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<ProcessToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<WebToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppReadPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppWritePresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppSubtaskPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppSkillPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AskUserPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppNavigatePresentationAdapter>()
            .Transient<ToolPresentations, ToolResultModelProjector, ToolResultCodec, ConnectionContextLimitsResolver,
                UnifiedDiff>();
}

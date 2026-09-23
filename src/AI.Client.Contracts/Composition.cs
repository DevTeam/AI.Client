// ReSharper disable UnusedMember.Local
namespace AI.Client.Contracts;

using System.Diagnostics;
using Pure.DI;
using Settings;
using Tools;
using Workspace;

/// <summary>
/// The presentation and codec services both sides of the Host-Web contract share. Nothing is
/// generated here: every consumer links this file into its own project and builds on it with
/// <c>DependsOn("AI.Client.Contracts.Composition")</c>, so Host and Web describe tool calls with
/// the same adapter set instead of two hand-kept copies that drift apart.
/// </summary>
internal sealed class Composition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup(kind: CompositionKind.Internal)
            // The generic adapter answers for every tool, so it is bound by its own type and handed
            // over as the fallback. Were it an IToolPresentationAdapter binding, the collection
            // would contain it too — first — and it would shadow every specific adapter.
            .Bind<GenericToolPresentationAdapter>().As(Lifetime.Singleton).To<GenericToolPresentationAdapter>()
            .Bind<IToolPresentations>().As(Lifetime.Singleton).To((
                GenericToolPresentationAdapter genericAdapter,
                IReadOnlyCollection<IToolPresentationAdapter> adapters) => new ToolPresentations(genericAdapter, adapters))
            .Bind<IToolResultModelProjector>().As(Lifetime.Singleton).To<ToolResultModelProjector>()
            .Bind<IToolResultCodec>().As(Lifetime.Singleton).To<ToolResultCodec>()
            .Bind<IConnectionContextLimitsResolver>().As(Lifetime.Singleton).To<ConnectionContextLimitsResolver>()
            .Bind<IUnifiedDiffParser>().As(Lifetime.Singleton).To<UnifiedDiff>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<FileToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<ProcessToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<WebToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppReadPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppWritePresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppSubtaskPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AskUserPresentationAdapter>();
}

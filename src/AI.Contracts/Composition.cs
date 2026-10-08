// DI guide: [Pure.DI conventions](../../docs/30-dependency-injection.md).
// ReSharper disable UnusedMember.Local
namespace AI.Contracts;

using System.Diagnostics;
using FileSystem;
using Pure.DI;
using Settings;
using Tools;
using Workspace;

/// <summary>
/// The presentation and codec services both sides of the Host-Web contract share, plus the file
/// system every executable performs its IO through. Nothing is generated here: every consumer links
/// this file into its own project and builds on it with
/// <c>DependsOn("AI.Contracts.Composition")</c>, so Host and Web describe tool calls with
/// the same adapter set instead of two hand-kept copies that drift apart.
/// </summary>
internal sealed class Composition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup(kind: CompositionKind.Internal)
            .Hint(Hint.Comments, "Off")
            // The file system is bound once, here, so that every executable linking this setup
            // performs its IO through the same contract and tests replace it in one place.
            // Stateless by nature, so transient: an owner that wants one instance keeps it.
            //
            // This simplified registration already publishes the contracts: it registers each
            // implementation together with the direct abstract contracts it supports, so IFileSystem,
            // IPath and IAtomicFileWriter resolve in every graph that depends on this setup. Binding
            // those three again next to this line is not an addition but an override, and Pure.DI
            // rejects it with DIW000 (measured in AI.Web, where it is an error). A graph that cannot
            // resolve them is a graph that never reaches this setup — the fix belongs there, as
            // DependsOn("AI.Contracts.Composition"), not as a second registration here.
            .Transient<SystemFileSystem, SystemPath, AtomicFileWriter>()
            .Bind<IGenericToolPresentationAdapter>().To<GenericToolPresentationAdapter>()
            // The adapter interface is inherited from a base class; bind that contract explicitly.
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<FileToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<ProcessToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<WebToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<CSharpToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppReadPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppWritePresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppSubtaskPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppSkillPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AskUserPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppNavigatePresentationAdapter>()
            .Transient<ToolPresentations, ToolResultModelProjector, ToolResultCodec, ConnectionContextLimitsResolver,
                ToolDefaultDecision,
                UnifiedDiff, Chats.ChatTeamRosterCalculator, Schedules.ScheduleCalendar, Schedules.ScheduleDescriptions>();
}

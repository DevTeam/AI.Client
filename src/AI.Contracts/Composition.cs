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
            .Bind<IToolPresentationAdapter>(Tag.Unique)
                .To<FileToolPresentationAdapter, ProcessToolPresentationAdapter, WebToolPresentationAdapter, CSharpToolPresentationAdapter,
                AppReadPresentationAdapter, AppWritePresentationAdapter, AppSubtaskPresentationAdapter, AppSkillPresentationAdapter, AskUserPresentationAdapter,
                AppNavigatePresentationAdapter, AppThemePresentationAdapter, AppPlanPresentationAdapter>()
            .Transient<ToolPresentations, ToolResultModelProjector, ToolResultCodec, ConnectionContextLimitsResolver, ToolDefaultDecision, UnifiedDiff,
                Chats.ChatTeamRosterCalculator, Schedules.ScheduleCalendar, Schedules.ScheduleDescriptions, SystemFileSystem, SystemPath, AtomicFileWriter,
                GenericToolPresentationAdapter>();
}

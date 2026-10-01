namespace AI.Web.Components;

/// <summary>
/// What a transcript turn is drawn from, apart from its own messages. <see cref="Shared"/> is the
/// state every turn shows (expansions, reviews, the token figures switch);
/// <see cref="Marks"/> are the single messages the transcript singles out, kept only by the turn
/// that holds them; <see cref="RunPresentationVersion"/> the run statuses, for the turns that show
/// them; <see cref="Usage"/> is what the turn itself used, and <see cref="Checkpoint"/>
/// the summary mark drawn above it, if any; <see cref="Live"/> moves only for the turn at the head
/// of the branch, which is the one the run is writing into.
/// </summary>
public readonly record struct TranscriptTurnStamp(TranscriptSharedStamp Shared, TranscriptTurnMarks Marks, int RunPresentationVersion, int BranchPickers,
    AI.Contracts.Usage.TokenUsageTotals? Usage, AI.Contracts.Chats.HistoryCheckpoint? Checkpoint, TranscriptLiveStamp? Live);

public readonly record struct TranscriptSharedStamp(
    Guid ChatId,
    int ExpandedTurnsVersion,
    int ReviewVersion,
    bool ShowTurnTokens);

/// <summary>
/// The branch's leaf moves with every message a run writes, and the turns above it do not show it:
/// each of these is set only when the turn holds that message.
/// </summary>
public readonly record struct TranscriptTurnMarks(Guid? BranchLeafId, Guid? JustArrivedLeafId, Guid? ForkSourceId, Guid? BranchMenuId);

public readonly record struct TranscriptLiveStamp(long ChatRevision, int? ElapsedSeconds, int LiveTextVersion);

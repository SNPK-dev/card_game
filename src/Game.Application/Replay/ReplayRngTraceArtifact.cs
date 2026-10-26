using Game.Shared.Events;

namespace Game.Application.Replay;

public sealed record ReplayRngTraceArtifact(
  string TraceVersion,
  string TraceHash,
  IReadOnlyList<ReplayRngTraceEntry> Rolls);

public sealed record ReplayRngTraceEntry(
  string RngRollId,
  string Stream,
  string PreRngState,
  string PostRngState,
  int CandidateCount,
  int SelectedIndex,
  string Reason,
  long CommandSeq,
  long EventSeq,
  IReadOnlyList<EventRef> SelectedTargetRefs);

using Game.Shared.Events;

namespace Game.Application.Replay;

public sealed record ReplayDebugTraceArtifact(
  string TraceVersion,
  string TraceHash,
  IReadOnlyList<ReplayDebugTraceEntry> Entries);

public sealed record ReplayDebugTraceEntry(
  long EventSeq,
  long? ParentEventSeq,
  long? CommandSeq,
  string EventType,
  string Phase,
  IReadOnlyList<EventRef> SourceRefs,
  IReadOnlyList<EventRef> TargetRefs,
  IReadOnlyList<string> MutationRefs,
  IReadOnlyList<string> RngRollRefs);

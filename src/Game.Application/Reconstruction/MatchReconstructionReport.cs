using Game.Shared.Events;

namespace Game.Application.Reconstruction;

public sealed record MatchReconstructionReport(
  string MatchId,
  int CommandCount,
  int EventCount,
  string FinalStateHash,
  string TerminalReason,
  string? WinnerPlayerId,
  IReadOnlyList<ReconstructedTurn> Turns,
  IReadOnlyList<ReconstructedAssetRef> AssetRefs);

public sealed record ReconstructedTurn(
  int TurnNumber,
  string? ActivePlayerId,
  IReadOnlyList<ReconstructedCommand> Commands);

public sealed record ReconstructedCommand(
  int CommandSeq,
  string? PlayerId,
  string CommandType,
  IReadOnlyDictionary<string, int> Payload,
  IReadOnlyDictionary<string, string> Refs,
  IReadOnlyList<ReconstructedEvent> Events,
  IReadOnlyList<ReconstructedDelta> Deltas);

public sealed record ReconstructedEvent(
  long EventSeq,
  CommittedEventType EventType,
  string? MutationBatchRef,
  IReadOnlyList<string> MutationRefs,
  string PreStateHash,
  string PostStateHash);

public sealed record ReconstructedDelta(
  string DeltaType,
  string SubjectRef,
  string? PlayerId,
  string Description,
  IReadOnlyDictionary<string, string> Metadata);

public sealed record ReconstructedAssetRef(
  string AssetManifestId,
  string CardId,
  string CardVersionId,
  string VisualMode,
  string ContentHash);

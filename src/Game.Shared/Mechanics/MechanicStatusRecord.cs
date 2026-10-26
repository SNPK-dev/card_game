namespace Game.Shared.Mechanics;

public sealed record MechanicStatusRecord(
  string MechanicId,
  MechanicStatus CurrentStatus,
  MechanicTier Tier,
  string Owner,
  IReadOnlyList<StatusTransition> StatusHistory,
  IReadOnlyList<string> ApprovedRulesetProfileIds,
  string? DeprecatedSince,
  string? RejectionReason,
  IReadOnlyList<string> LinkedSpecs,
  IReadOnlyList<string> LinkedTests,
  IReadOnlyList<string> LinkedCoverageExamples,
  IReadOnlyList<string> RiskFlags);

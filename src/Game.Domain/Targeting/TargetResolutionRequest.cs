using Game.Shared.Dsl;
using Game.Domain.Randomness;

namespace Game.Domain.Targeting;

public sealed record TargetResolutionRequest(
  TargetSpec TargetSpec,
  string ActingPlayerId,
  string? SourceControllerPlayerId = null,
  IReadOnlyList<ResolvedTargetRef>? ChosenTargets = null,
  ResolvedTargetRef? EventSource = null,
  ResolvedTargetRef? EventTarget = null,
  IReadOnlyList<ResolvedTargetRef>? PreviousActionResults = null,
  IRngRoller? RngRoller = null,
  int? CommandSeq = null,
  string? RngRollScope = null);

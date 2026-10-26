using Game.Domain.Pooling;
using Game.Domain.Randomness;
using Game.Domain.State;
using Game.Domain.Targeting;
using Game.Domain.History;
using Game.Shared.Dsl;

namespace Game.Domain.Effects;

public sealed record EffectActionCompilationRequest(
  IGameState State,
  EffectDefinition Effect,
  string ActingPlayerId,
  string SourceControllerPlayerId,
  string SourceRef,
  int CommandSeq,
  string PreStateHash,
  IReadOnlyList<ResolvedTargetRef>? ChosenTargets = null,
  ResolvedTargetRef? Source = null,
  IReadOnlyDictionary<string, FixedPoolDefinition>? Pools = null,
  IRngRoller? RngRoller = null,
  int? MaxBoardSize = null,
  HistoryStore? HistoryStore = null);

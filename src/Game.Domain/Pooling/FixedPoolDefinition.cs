namespace Game.Domain.Pooling;

public sealed record FixedPoolDefinition(
  string PoolId,
  string PoolType,
  IReadOnlyList<PoolItemRef> Items,
  int? MaxCandidateCount = null,
  string Ordering = FixedPoolOrdering.Canonical);

namespace Game.Domain.Pooling;

public enum PoolResolutionErrorCode
{
  MissingPoolId,
  UnsupportedPoolType,
  UnsupportedOrdering,
  EmptyPool,
  DuplicateCandidate,
  InvalidCandidate,
  InvalidMaxCandidateCount
}

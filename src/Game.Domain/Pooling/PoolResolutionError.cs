namespace Game.Domain.Pooling;

public sealed record PoolResolutionError(
  PoolResolutionErrorCode Code,
  string Message,
  string? PoolId = null,
  string? CandidateId = null);

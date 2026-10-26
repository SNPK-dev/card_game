namespace Game.Domain.Pooling;

public sealed class PoolResolver : IPoolResolver
{
  public const string FixedPoolType = "FIXED_POOL";

  public PoolResolutionResult Resolve(PoolResolutionRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.Pool);

    var pool = request.Pool;
    if (string.IsNullOrWhiteSpace(pool.PoolId))
    {
      return PoolResolutionResult.Rejected(
        string.Empty,
        new PoolResolutionError(
          PoolResolutionErrorCode.MissingPoolId,
          "Pool id is required."));
    }

    if (!string.Equals(pool.PoolType, FixedPoolType, StringComparison.Ordinal))
    {
      return PoolResolutionResult.Rejected(
        pool.PoolId,
        new PoolResolutionError(
          PoolResolutionErrorCode.UnsupportedPoolType,
          $"Pool type '{pool.PoolType}' is not supported by bootstrap pool resolution.",
          pool.PoolId));
    }

    if (pool.MaxCandidateCount is <= 0)
    {
      return PoolResolutionResult.Rejected(
        pool.PoolId,
        new PoolResolutionError(
          PoolResolutionErrorCode.InvalidMaxCandidateCount,
          "Max candidate count must be greater than zero when provided.",
          pool.PoolId));
    }

    if (string.Equals(pool.Ordering, FixedPoolOrdering.Random, StringComparison.Ordinal))
    {
      return PoolResolutionResult.Rejected(
        pool.PoolId,
        new PoolResolutionError(
          PoolResolutionErrorCode.UnsupportedOrdering,
          "Random pool ordering requires deterministic RNG roll logging before it can be resolved.",
          pool.PoolId));
    }

    var invalid = pool.Items.FirstOrDefault(item =>
      string.IsNullOrWhiteSpace(item.ItemKind) || string.IsNullOrWhiteSpace(item.ItemId));
    if (invalid is not null)
    {
      return PoolResolutionResult.Rejected(
        pool.PoolId,
        new PoolResolutionError(
          PoolResolutionErrorCode.InvalidCandidate,
          "Pool candidates require item kind and item id.",
          pool.PoolId,
          invalid.ItemId));
    }

    var duplicate = pool.Items
      .GroupBy(item => (Normalize(item.ItemKind), item.ItemId), StringTupleComparer.Ordinal)
      .FirstOrDefault(group => group.Count() > 1);
    if (duplicate is not null)
    {
      return PoolResolutionResult.Rejected(
        pool.PoolId,
        new PoolResolutionError(
          PoolResolutionErrorCode.DuplicateCandidate,
          "Fixed pool candidates must be unique by kind and id.",
          pool.PoolId,
          duplicate.Key.Item2));
    }

    if (pool.Items.Count == 0)
    {
      return PoolResolutionResult.Rejected(
        pool.PoolId,
        new PoolResolutionError(
          PoolResolutionErrorCode.EmptyPool,
          "Fixed pool has no candidates.",
          pool.PoolId));
    }

    var ordered = pool.Items
      .Select(item => item with { ItemKind = Normalize(item.ItemKind) })
      .OrderBy(item => item.ItemKind, StringComparer.Ordinal)
      .ThenBy(item => item.ItemId, StringComparer.Ordinal)
      .ToArray();

    if (pool.MaxCandidateCount is { } maxCandidateCount)
    {
      ordered = ordered.Take(maxCandidateCount).ToArray();
    }

    return ordered.Length == 0
      ? PoolResolutionResult.Rejected(
        pool.PoolId,
        new PoolResolutionError(
          PoolResolutionErrorCode.EmptyPool,
          "Fixed pool has no candidates after applying max candidate count.",
          pool.PoolId))
      : PoolResolutionResult.Accepted(pool.PoolId, ordered);
  }

  private static string Normalize(string value)
  {
    return value.Trim().ToUpperInvariant();
  }

  private sealed class StringTupleComparer : IEqualityComparer<(string, string)>
  {
    public static StringTupleComparer Ordinal { get; } = new();

    public bool Equals((string, string) x, (string, string) y)
    {
      return StringComparer.Ordinal.Equals(x.Item1, y.Item1)
        && StringComparer.Ordinal.Equals(x.Item2, y.Item2);
    }

    public int GetHashCode((string, string) obj)
    {
      return HashCode.Combine(
        StringComparer.Ordinal.GetHashCode(obj.Item1),
        StringComparer.Ordinal.GetHashCode(obj.Item2));
    }
  }
}

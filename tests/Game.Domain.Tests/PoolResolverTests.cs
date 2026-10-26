using Game.Domain.Pooling;
using Xunit;

namespace Game.Domain.Tests;

public sealed class PoolResolverTests
{
  [Fact]
  public void FixedPoolResolvesCandidatesInCanonicalOrder()
  {
    var resolver = new PoolResolver();
    var pool = FixedPool(
      [
        new PoolItemRef("spell", "zap"),
        new PoolItemRef("CARD", "alpha"),
        new PoolItemRef("card", "beta")
      ]);

    var result = resolver.Resolve(new PoolResolutionRequest(pool));

    Assert.True(result.IsAccepted);
    Assert.Equal(
      ["CARD:alpha", "CARD:beta", "SPELL:zap"],
      result.Candidates.Select(candidate => $"{candidate.ItemKind}:{candidate.ItemId}"));
  }

  [Fact]
  public void FixedPoolAppliesMaxCandidateCountAfterCanonicalOrdering()
  {
    var resolver = new PoolResolver();
    var pool = FixedPool(
      [
        new PoolItemRef("SPELL", "zap"),
        new PoolItemRef("CARD", "beta"),
        new PoolItemRef("CARD", "alpha")
      ],
      maxCandidateCount: 2);

    var result = resolver.Resolve(new PoolResolutionRequest(pool));

    Assert.True(result.IsAccepted);
    Assert.Equal(["alpha", "beta"], result.Candidates.Select(candidate => candidate.ItemId));
  }

  [Fact]
  public void EmptyFixedPoolIsRejected()
  {
    var resolver = new PoolResolver();
    var pool = FixedPool([]);

    var result = resolver.Resolve(new PoolResolutionRequest(pool));

    Assert.False(result.IsAccepted);
    Assert.Contains(result.Errors, error => error.Code == PoolResolutionErrorCode.EmptyPool);
  }

  [Fact]
  public void DuplicateFixedPoolCandidatesAreRejected()
  {
    var resolver = new PoolResolver();
    var pool = FixedPool(
      [
        new PoolItemRef("card", "alpha"),
        new PoolItemRef("CARD", "alpha")
      ]);

    var result = resolver.Resolve(new PoolResolutionRequest(pool));

    Assert.False(result.IsAccepted);
    Assert.Contains(result.Errors, error => error.Code == PoolResolutionErrorCode.DuplicateCandidate);
  }

  [Fact]
  public void UnsupportedPoolTypeIsRejected()
  {
    var resolver = new PoolResolver();
    var pool = FixedPool([new PoolItemRef("CARD", "alpha")]) with
    {
      PoolType = "CATALOG_POOL"
    };

    var result = resolver.Resolve(new PoolResolutionRequest(pool));

    Assert.False(result.IsAccepted);
    Assert.Contains(result.Errors, error => error.Code == PoolResolutionErrorCode.UnsupportedPoolType);
  }

  [Fact]
  public void NonPositiveMaxCandidateCountIsRejected()
  {
    var resolver = new PoolResolver();
    var pool = FixedPool(
      [new PoolItemRef("CARD", "alpha")],
      maxCandidateCount: 0);

    var result = resolver.Resolve(new PoolResolutionRequest(pool));

    Assert.False(result.IsAccepted);
    Assert.Contains(result.Errors, error => error.Code == PoolResolutionErrorCode.InvalidMaxCandidateCount);
  }

  [Fact]
  public void RandomOrderingIsRejectedUntilDeterministicRollLoggingExists()
  {
    var resolver = new PoolResolver();
    var pool = FixedPool(
      [new PoolItemRef("CARD", "alpha")],
      ordering: FixedPoolOrdering.Random);

    var result = resolver.Resolve(new PoolResolutionRequest(pool));

    Assert.False(result.IsAccepted);
    Assert.Contains(result.Errors, error => error.Code == PoolResolutionErrorCode.UnsupportedOrdering);
  }

  private static FixedPoolDefinition FixedPool(
    IReadOnlyList<PoolItemRef> items,
    int? maxCandidateCount = null,
    string ordering = FixedPoolOrdering.Canonical)
  {
    return new FixedPoolDefinition(
      "pool-1",
      PoolResolver.FixedPoolType,
      items,
      maxCandidateCount,
      ordering);
  }
}

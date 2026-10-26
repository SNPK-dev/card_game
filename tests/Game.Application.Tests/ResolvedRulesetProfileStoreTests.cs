using Game.Application.Rulesets;
using Game.Shared.Ruleset;
using Xunit;

namespace Game.Application.Tests;

public sealed class ResolvedRulesetProfileStoreTests
{
  [Fact]
  public void StorePreservesProfilesAndReturnsLatestByInsertionOrder()
  {
    var first = TestRulesetProfileFactory.CreateProfile("resolved-alpha", resolvedHash: "hash-a");
    var second = TestRulesetProfileFactory.CreateProfile("resolved-beta", resolvedHash: "hash-b");
    var store = new InMemoryResolvedRulesetProfileStore();
    var query = new ResolvedRulesetProfileQueryService(store);

    var firstResult = store.Store(first);
    var secondResult = store.Store(second);

    Assert.True(firstResult.Succeeded);
    Assert.True(secondResult.Succeeded);
    Assert.Equal(["resolved-alpha", "resolved-beta"], store.Profiles.Select(profile => profile.Id));
    Assert.Same(second, store.Latest);
    Assert.Same(second, query.GetLatestProfile());
    Assert.Same(first, query.GetProfile("resolved-alpha"));
    Assert.Same(first, query.GetProfileBySourceAndHash("ruleset_mvp_core_v1", "hash-a"));
  }

  [Fact]
  public void StoreRejectsDuplicateProfileIdAndPreservesOriginal()
  {
    var original = TestRulesetProfileFactory.CreateProfile("resolved-alpha", resolvedHash: "hash-a");
    var duplicate = TestRulesetProfileFactory.CreateProfile("resolved-alpha", resolvedHash: "hash-b");
    var store = new InMemoryResolvedRulesetProfileStore();

    var firstResult = store.Store(original);
    var duplicateResult = store.Store(duplicate);

    Assert.True(firstResult.Succeeded);
    Assert.False(duplicateResult.Succeeded);
    Assert.Contains("resolved-alpha", duplicateResult.Error, StringComparison.Ordinal);
    Assert.Single(store.Profiles);
    Assert.Same(original, store.FindById("resolved-alpha"));
    Assert.Null(store.FindBySourceProfileAndHash("ruleset_mvp_core_v1", "hash-b"));
  }
}

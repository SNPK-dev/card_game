using Game.Application.Rulesets;
using Game.Shared.Ruleset;

namespace Game.Infrastructure.Storage;

public sealed class JsonFileResolvedRulesetProfileStore : IResolvedRulesetProfileStore
{
  private readonly JsonFileCollectionStore<ResolvedRulesetProfile> store;

  public JsonFileResolvedRulesetProfileStore(string rootDirectory)
  {
    store = new JsonFileCollectionStore<ResolvedRulesetProfile>(
      Path.Combine(rootDirectory, "resolved-ruleset-profiles.json"),
      profile => profile.Id);
  }

  public ResolvedRulesetProfile? Latest => store.Latest;

  public IReadOnlyList<ResolvedRulesetProfile> Profiles => store.Items;

  public ResolvedRulesetProfileStoreResult Store(ResolvedRulesetProfile profile) =>
    store.TryStore(profile)
      ? ResolvedRulesetProfileStoreResult.Success
      : ResolvedRulesetProfileStoreResult.DuplicateProfileId(profile.Id);

  public ResolvedRulesetProfile? FindById(string profileId) => store.FindById(profileId);

  public ResolvedRulesetProfile? FindBySourceProfileAndHash(string sourceProfileId, string resolvedHash) =>
    Profiles.FirstOrDefault(profile =>
      string.Equals(profile.SourceProfileId, sourceProfileId, StringComparison.Ordinal)
      && string.Equals(profile.ResolvedHash, resolvedHash, StringComparison.Ordinal));
}

using Game.Shared.Ruleset;

namespace Game.Application.Rulesets;

public sealed class ResolvedRulesetProfileQueryService(IResolvedRulesetProfileStore store)
{
  public ResolvedRulesetProfile? GetLatestProfile() => store.Latest;

  public ResolvedRulesetProfile? GetProfile(string profileId) => store.FindById(profileId);

  public ResolvedRulesetProfile? GetProfileBySourceAndHash(
    string sourceProfileId,
    string resolvedHash) =>
    store.FindBySourceProfileAndHash(sourceProfileId, resolvedHash);
}

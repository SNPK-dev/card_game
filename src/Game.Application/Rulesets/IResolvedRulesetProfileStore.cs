using Game.Shared.Ruleset;

namespace Game.Application.Rulesets;

public interface IResolvedRulesetProfileStore
{
  ResolvedRulesetProfile? Latest { get; }

  IReadOnlyList<ResolvedRulesetProfile> Profiles { get; }

  ResolvedRulesetProfileStoreResult Store(ResolvedRulesetProfile profile);

  ResolvedRulesetProfile? FindById(string profileId);

  ResolvedRulesetProfile? FindBySourceProfileAndHash(string sourceProfileId, string resolvedHash);
}

using Game.Shared.Ruleset;

namespace Game.Application.Rulesets;

public sealed class InMemoryResolvedRulesetProfileStore : IResolvedRulesetProfileStore
{
  private readonly List<ResolvedRulesetProfile> profiles = [];
  private readonly HashSet<string> profileIds = new(StringComparer.Ordinal);

  public ResolvedRulesetProfile? Latest => profiles.Count == 0 ? null : profiles[^1];

  public IReadOnlyList<ResolvedRulesetProfile> Profiles => profiles.ToArray();

  public ResolvedRulesetProfileStoreResult Store(ResolvedRulesetProfile profile)
  {
    ArgumentNullException.ThrowIfNull(profile);

    if (!profileIds.Add(profile.Id))
    {
      return ResolvedRulesetProfileStoreResult.DuplicateProfileId(profile.Id);
    }

    profiles.Add(profile);
    return ResolvedRulesetProfileStoreResult.Success;
  }

  public ResolvedRulesetProfile? FindById(string profileId) =>
    profiles.FirstOrDefault(profile => string.Equals(profile.Id, profileId, StringComparison.Ordinal));

  public ResolvedRulesetProfile? FindBySourceProfileAndHash(string sourceProfileId, string resolvedHash) =>
    profiles.FirstOrDefault(profile =>
      string.Equals(profile.SourceProfileId, sourceProfileId, StringComparison.Ordinal)
      && string.Equals(profile.ResolvedHash, resolvedHash, StringComparison.Ordinal));
}

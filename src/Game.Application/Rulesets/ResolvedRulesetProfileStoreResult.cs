namespace Game.Application.Rulesets;

public sealed record ResolvedRulesetProfileStoreResult(
  bool Succeeded,
  string? Error)
{
  public static ResolvedRulesetProfileStoreResult Success { get; } = new(true, null);

  public static ResolvedRulesetProfileStoreResult DuplicateProfileId(string profileId) =>
    new(false, $"Resolved ruleset profile '{profileId}' already exists.");
}

namespace Game.Shared.Ruleset;

public sealed record RulesetProfileMetadata(
  string CreatedBy,
  DateTimeOffset CreatedAt,
  string UpdatedBy,
  DateTimeOffset UpdatedAt,
  IReadOnlyList<string> Notes);

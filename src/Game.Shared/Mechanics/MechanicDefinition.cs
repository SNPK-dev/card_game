namespace Game.Shared.Mechanics;

public sealed record MechanicDefinition(
  string MechanicId,
  string DisplayName,
  string Description,
  MechanicTier Tier,
  MechanicStatus Status,
  string Owner,
  IReadOnlyList<string> RelatedEffectKinds,
  IReadOnlyList<string> RelatedActions,
  IReadOnlyList<string> RelatedKeywords,
  IReadOnlyList<string> RelatedSpecs,
  IReadOnlyList<string> CoverageExamples,
  IReadOnlyList<string> RiskFlags,
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt);

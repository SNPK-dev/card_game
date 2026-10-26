using Game.Shared.Ruleset;

namespace Game.Generator.Generation;

public sealed record GenerationContext(
  string RulesetProfileId,
  string RulesetProfileHash,
  string? IntentId,
  string? CatalogSnapshotId,
  IReadOnlySet<string> AllowedMechanics,
  IReadOnlySet<string> AllowedActions,
  IReadOnlySet<string> AllowedEffectKinds,
  IReadOnlySet<string> AllowedSourceZones,
  IReadOnlySet<string> AllowedPhases,
  IReadOnlySet<string> AllowedPools,
  IReadOnlySet<string> AllowedCardTypes,
  IReadOnlySet<string> AllowedRarities,
  IReadOnlySet<string> AllowedClasses,
  IReadOnlySet<string> AllowedTribes,
  IReadOnlySet<string> AllowedKeywords,
  IReadOnlyList<GenerationWhitelistManifest> WhitelistManifests,
  GenerationLimits Limits,
  BalancePolicyRef BalancePolicy,
  SimulationPolicyRef SimulationPolicy,
  PublicationPolicyRef PublicationPolicy);

public sealed record GenerationLimits(
  int MaxComplexityScore,
  int MaxRandomnessScore,
  int MaxActionsPerEffect,
  int MaxEffectsPerCard);

public sealed record GenerationWhitelistManifest(
  string ManifestId,
  string Kind,
  IReadOnlyList<string> Values);

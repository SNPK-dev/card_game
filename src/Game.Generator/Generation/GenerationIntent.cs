namespace Game.Generator.Generation;

public sealed record GenerationIntent(
  string IntentId,
  string? CatalogSnapshotId,
  IReadOnlyList<string> RequestedMechanics,
  IReadOnlyList<string> RequestedActions,
  IReadOnlyList<string> RequestedEffectKinds,
  IReadOnlyList<string> RequestedSourceZones,
  IReadOnlyList<string> RequestedPhases,
  IReadOnlyList<string> RequestedPools,
  IReadOnlyList<string> RequestedCardTypes,
  IReadOnlyList<string> RequestedRarities,
  IReadOnlyList<string> RequestedClasses,
  IReadOnlyList<string> RequestedTribes,
  IReadOnlyList<string> RequestedKeywords);

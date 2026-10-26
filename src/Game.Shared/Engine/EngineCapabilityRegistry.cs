namespace Game.Shared.Engine;

public sealed record EngineCapabilityRegistry(
  string EngineVersion,
  IReadOnlyList<string> SupportedSchemaVersions,
  IReadOnlyList<string> SupportedPrimitiveRegistryVersions,
  IReadOnlyList<string> SupportedRulesetProfileVersions,
  IReadOnlyList<string> SupportedActionTypes,
  IReadOnlyList<string> SupportedEffectKinds,
  IReadOnlyList<string> SupportedMechanics,
  IReadOnlyList<string> SupportedPhases,
  IReadOnlyList<string> SupportedSourceZones,
  IReadOnlyList<string> SupportedResolutionPolicies,
  IReadOnlyList<string> SupportedFizzlePolicies,
  IReadOnlyList<string> SupportedAssetManifestVersions,
  EngineCapabilityMetadata Metadata);

public sealed record EngineCapabilityMetadata(
  string AuthoredBy,
  DateTimeOffset AuthoredAt,
  string ReviewedBy,
  DateTimeOffset ReviewedAt,
  IReadOnlyList<string> SourceReferences,
  IReadOnlyList<string> Notes);

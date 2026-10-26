namespace Game.Application.Capabilities;

public sealed record RuntimeCapabilityDeclaration(
  string DeclarationId,
  IReadOnlyList<string>? SchemaVersions = null,
  IReadOnlyList<string>? PrimitiveRegistryVersions = null,
  IReadOnlyList<string>? RulesetProfileVersions = null,
  IReadOnlyList<string>? ActionTypes = null,
  IReadOnlyList<string>? EffectKinds = null,
  IReadOnlyList<string>? Mechanics = null,
  IReadOnlyList<string>? Phases = null,
  IReadOnlyList<string>? SourceZones = null,
  IReadOnlyList<string>? ResolutionPolicies = null,
  IReadOnlyList<string>? FizzlePolicies = null,
  IReadOnlyList<string>? AssetManifestVersions = null,
  IReadOnlyList<string>? MutationTypes = null,
  IReadOnlyList<string>? EventMappedMutationTypes = null,
  IReadOnlyList<string>? RequiredMutationTypes = null,
  IReadOnlyList<string>? SourceReferences = null,
  IReadOnlyList<string>? Notes = null);

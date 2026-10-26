using Game.Shared.Primitives;

namespace Game.Shared.Dsl;

public sealed record CardDefinition(
  string? CardId,
  string? CardVersion,
  string SchemaVersion,
  string EngineVersion,
  string PrimitiveRegistryVersion,
  string RulesetProfileId,
  string? CatalogSnapshotId,
  StaticCardData StaticData,
  EntityDefinition Entity,
  IReadOnlyList<EffectDefinition> Effects,
  CardTextGeneration CardText,
  BalanceMetadata BalanceMetadata,
  AssetMetadata AssetMetadata,
  AuthoringMetadata AuthoringMetadata);

public sealed record StaticCardData(
  string Name,
  CardType CardType,
  IReadOnlyList<string> ClassTags,
  Rarity Rarity,
  int ManaCost,
  IReadOnlyList<string> Tribes,
  IReadOnlyList<string> Keywords,
  bool Collectible,
  IReadOnlyList<string> FormatTags);

public sealed record CardTextGeneration(
  string RulesText,
  string? FlavorText);

public sealed record BalanceMetadata(
  int ComplexityScore,
  int RandomnessScore,
  IReadOnlyList<string> RiskFlags);

public sealed record AssetMetadata(
  string? AssetManifestId,
  string? VisualAssetRef,
  string? AudioManifestId);

public sealed record AuthoringMetadata(
  string CreatedBy,
  DateTimeOffset CreatedAt,
  string? GenerationPipelineVersion,
  IReadOnlyList<string> SourceRefs);

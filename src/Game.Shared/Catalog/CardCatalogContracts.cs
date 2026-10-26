using Game.Shared.Dsl;
using System.Text.Json.Serialization;

namespace Game.Shared.Catalog;

public sealed record CardVersion(
  string CardId,
  string CardVersionId,
  string SchemaVersion,
  string EngineVersion,
  string CatalogSnapshotId,
  string GenerationPipelineVersion,
  string RulesetProfileId,
  string RulesetProfileHash,
  DateTimeOffset PublishedAt,
  CardDefinition Definition,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  bool? Published = null);

public sealed record CatalogSnapshot(
  string SnapshotId,
  string RulesetProfileId,
  string RulesetProfileHash,
  DateTimeOffset CreatedAt,
  IReadOnlyList<CardVersion> Cards,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  IReadOnlyList<string>? CompatibleRulesetIds = null,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  IReadOnlyList<string>? CompatibleRulesetVersions = null,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  bool ProductionScaleCompatible = false);

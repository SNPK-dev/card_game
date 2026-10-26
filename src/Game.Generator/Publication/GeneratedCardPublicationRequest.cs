using Game.Generator.Validation;
using Game.Shared.Engine;

namespace Game.Generator.Publication;

public sealed record GeneratedCardPublicationRequest(
  GeneratedCardValidationRequest ValidationRequest,
  GeneratedCardValidationPipelineReport ValidationReport,
  string CardVersionId,
  string CatalogSnapshotId,
  DateTimeOffset PublishedAt,
  EngineCapabilityRegistry EngineCapabilityRegistry);

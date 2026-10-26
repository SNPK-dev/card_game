using Game.Shared.Catalog;
using Game.Shared.Engine;

namespace Game.Application.Catalog;

public sealed record CatalogCompatibilityRequest(
  CatalogSnapshot Snapshot,
  EngineCapabilityRegistry Registry,
  string? ExpectedRegistryHash = null);

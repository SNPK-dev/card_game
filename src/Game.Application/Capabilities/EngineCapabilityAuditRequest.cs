using Game.Shared.Catalog;
using Game.Shared.Engine;
using Game.Shared.Ruleset;

namespace Game.Application.Capabilities;

public sealed record EngineCapabilityAuditRequest(
  EngineCapabilityRegistry Registry,
  ResolvedRulesetProfile? RulesetProfile = null,
  CatalogSnapshot? CatalogSnapshot = null,
  string? ExpectedRegistryHash = null);

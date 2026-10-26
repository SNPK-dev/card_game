using Game.Shared.Dsl;
using Game.Shared.Engine;
using Game.Shared.Ruleset;

namespace Game.Application.Capabilities;

public sealed record EngineCapabilityCompatibilityRequest(
  EngineCapabilityRegistry Registry,
  ResolvedRulesetProfile? RulesetProfile,
  IReadOnlyList<CardDefinition> CardDefinitions);

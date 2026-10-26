using Game.Domain.State;
using Game.Shared.Catalog;
using Game.Shared.Engine;
using Game.Shared.Ruleset;

namespace Game.Application.Simulation;

public sealed record BotSimulationRequest(
  string SimulationId,
  IGameState InitialState,
  IReadOnlyList<CardVersion> CatalogCards,
  IReadOnlyDictionary<string, IBotAgent> BotsByPlayerId,
  BotSimulationOptions? Options = null,
  ResolvedRulesetProfile? RulesetProfile = null,
  EngineCapabilityRegistry? EngineCapabilities = null);

using Game.Domain.State;
using Game.Domain.Randomness;
using Game.Shared.Catalog;
using Game.Shared.Engine;
using Game.Shared.Ruleset;

namespace Game.Application.Simulation;

public sealed record BotDecisionContext(
  IGameState State,
  string PlayerId,
  IReadOnlyDictionary<string, CardVersion> CardVersionsByVersionId,
  int CommandSeq = 0,
  IRngRoller? BotRngRoller = null,
  ResolvedRulesetProfile? RulesetProfile = null,
  EngineCapabilityRegistry? EngineCapabilities = null);

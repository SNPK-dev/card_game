using Game.Domain.State;
using Game.Shared.Catalog;
using Game.Shared.Engine;
using Game.Shared.Ruleset;

namespace Game.Application.Legality;

public sealed record LegalActionQuery(
  IGameState State,
  string PlayerId,
  IReadOnlyDictionary<string, CardVersion> CardVersionsByVersionId,
  ResolvedRulesetProfile? RulesetProfile = null,
  EngineCapabilityRegistry? EngineCapabilities = null);

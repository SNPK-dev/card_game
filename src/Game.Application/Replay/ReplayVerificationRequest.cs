using Game.Domain.Replay;
using Game.Domain.State;
using Game.Shared.Catalog;
using Game.Shared.Engine;
using Game.Shared.Ruleset;

namespace Game.Application.Replay;

public sealed record ReplayVerificationRequest(
  IGameState InitialState,
  CanonicalCommandLog CommandLog,
  string? ExpectedFinalStateHash,
  string? ExpectedInitialStateHash = null,
  IReadOnlyList<string>? ExpectedRngRollRefs = null,
  ReplayRngTraceArtifact? ExpectedRngTrace = null,
  EngineCapabilityRegistry? EngineCapabilityRegistry = null,
  ResolvedRulesetProfile? RulesetProfile = null,
  CatalogSnapshot? CatalogSnapshot = null,
  string? ExpectedEngineCapabilityRegistryHash = null);

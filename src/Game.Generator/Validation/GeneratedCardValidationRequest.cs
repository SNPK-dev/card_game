using Game.Generator.Generation;
using Game.Shared.Dsl;

namespace Game.Generator.Validation;

public sealed record GeneratedCardValidationRequest(
  string GenerationId,
  string PipelineVersion,
  string ContextVersion,
  CardDefinition Candidate,
  GenerationContext Context,
  GeneratedCardBalanceReport? BalanceReport = null,
  GeneratedCardSimulationReport? SimulationReport = null,
  GeneratedCardReplayReport? ReplayReport = null);

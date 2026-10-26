using Game.Generator.Generation;
using Game.Shared.Dsl;

namespace Game.Generator.Effects;

public sealed record GeneratedCardEffectExpansionRequest(
  string ExpansionId,
  CardDefinition Candidate,
  GenerationContext Context,
  EffectDefinition Effect,
  string RulesText,
  int ComplexityScore,
  int RandomnessScore,
  IReadOnlyList<string> RiskFlags);

using Game.Shared.Dsl;

namespace Game.Generator.Effects;

public sealed record GeneratedCardEffectExpansionResult(
  bool Succeeded,
  CardDefinition? Candidate,
  IReadOnlyList<GeneratedCardEffectExpansionDiagnostic> Diagnostics)
{
  public static GeneratedCardEffectExpansionResult Success(CardDefinition candidate) =>
    new(true, candidate, []);

  public static GeneratedCardEffectExpansionResult Rejected(
    IReadOnlyList<GeneratedCardEffectExpansionDiagnostic> diagnostics) =>
    new(false, null, diagnostics);
}

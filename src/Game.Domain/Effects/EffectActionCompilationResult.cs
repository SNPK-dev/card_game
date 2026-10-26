using Game.Domain.Mutation;
using Game.Domain.Randomness;
using Game.Domain.Targeting;

namespace Game.Domain.Effects;

public sealed record EffectActionRngRoll(
  RngRollRecord Roll,
  IReadOnlyList<ResolvedTargetRef> SelectedTargets);

public sealed record EffectActionCompilationResult(
  bool IsAccepted,
  ProposedMutationBatch? Batch,
  IReadOnlyList<EffectActionCompilationDiagnostic> Diagnostics,
  IReadOnlyList<EffectActionRngRoll> RngRolls)
{
  public static EffectActionCompilationResult Accepted(
    ProposedMutationBatch batch,
    IReadOnlyList<EffectActionRngRoll>? rngRolls = null)
  {
    return new EffectActionCompilationResult(true, batch, [], rngRolls ?? []);
  }

  public static EffectActionCompilationResult Rejected(
    params EffectActionCompilationDiagnostic[] diagnostics)
  {
    return new EffectActionCompilationResult(false, null, diagnostics, []);
  }
}

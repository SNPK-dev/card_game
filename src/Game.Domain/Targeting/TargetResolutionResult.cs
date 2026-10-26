namespace Game.Domain.Targeting;

using Game.Domain.Randomness;

public sealed record TargetResolutionResult(
  bool IsAccepted,
  IReadOnlyList<ResolvedTargetRef> Targets,
  IReadOnlyList<ResolvedTargetRef> Candidates,
  IReadOnlyList<TargetResolutionError> Errors,
  IReadOnlyList<RngRollRecord> RngRolls)
{
  public static TargetResolutionResult Accepted(
    IReadOnlyList<ResolvedTargetRef> targets,
    IReadOnlyList<ResolvedTargetRef> candidates,
    IReadOnlyList<RngRollRecord>? rngRolls = null)
  {
    return new TargetResolutionResult(true, targets, candidates, [], rngRolls ?? []);
  }

  public static TargetResolutionResult Rejected(
    IReadOnlyList<ResolvedTargetRef> candidates,
    params TargetResolutionError[] errors)
  {
    return new TargetResolutionResult(false, [], candidates, errors, []);
  }
}

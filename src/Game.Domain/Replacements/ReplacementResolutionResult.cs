using Game.Domain.Mutation;

namespace Game.Domain.Replacements;

public sealed record ReplacementResolutionResult(
  bool IsAccepted,
  ProposedMutationBatch? FinalBatch,
  IReadOnlyList<ReplacementDecision> Decisions,
  IReadOnlyList<ReplacementResolutionDiagnostic> Diagnostics)
{
  public static ReplacementResolutionResult Accepted(
    ProposedMutationBatch finalBatch,
    IReadOnlyList<ReplacementDecision> decisions) =>
    new(true, finalBatch, decisions, []);

  public static ReplacementResolutionResult Rejected(
    IReadOnlyList<ReplacementResolutionDiagnostic> diagnostics) =>
    new(false, null, [], diagnostics);
}

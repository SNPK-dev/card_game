namespace Game.Domain.Triggers;

public sealed record TriggerResolutionResult(
  bool IsAccepted,
  IReadOnlyList<TriggerMatch> Matches,
  IReadOnlyList<TriggerResolutionDiagnostic> Diagnostics)
{
  public static TriggerResolutionResult Accepted(IReadOnlyList<TriggerMatch> matches) =>
    new(true, matches, []);

  public static TriggerResolutionResult Rejected(IReadOnlyList<TriggerResolutionDiagnostic> diagnostics) =>
    new(false, [], diagnostics);
}

namespace Game.Domain.Replacements;

public sealed record ReplacementResolutionDiagnostic(
  ReplacementResolutionDiagnosticCode Code,
  string Message,
  string? ProposedEventId = null);

public enum ReplacementResolutionDiagnosticCode
{
  ProposedEventLimitExceeded,
  InvalidEventSequenceCandidate,
  MissingState,
  MissingProposedMutation,
  InvalidDamagePayload
}

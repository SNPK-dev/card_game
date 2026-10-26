namespace Game.Domain.Triggers;

public sealed record TriggerResolutionDiagnostic(
  TriggerResolutionDiagnosticCode Code,
  string Message,
  string? TriggerId = null,
  long? EventSeq = null);

public enum TriggerResolutionDiagnosticCode
{
  UnsupportedEventFilter,
  UnsupportedControllerScope
}

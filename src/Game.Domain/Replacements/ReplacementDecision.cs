using Game.Shared.Events;

namespace Game.Domain.Replacements;

public sealed record ReplacementDecision(
  string DecisionId,
  string ProposedEventId,
  long EventSeqCandidate,
  ProposedEventType ProposedEventType,
  ReplacementDecisionResult Result,
  string Reason);

public enum ReplacementDecisionResult
{
  Unchanged,
  Modified,
  Cancelled,
  Redirected,
  ReplacedWithOtherOperation
}

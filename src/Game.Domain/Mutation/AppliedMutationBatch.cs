using Game.Domain.Replacements;
using Game.Shared.Events;

namespace Game.Domain.Mutation;

public sealed record AppliedMutationBatch(
  string BatchId,
  int CommandSeq,
  int? ParentEventSeq,
  string Phase,
  string SourceRef,
  string Reason,
  IReadOnlyList<StateMutation> AppliedMutations,
  IReadOnlyList<StateMutation> SkippedMutations,
  string PreStateHash,
  string PostStateHash,
  IReadOnlyList<string> DerivedEventRefs,
  int AppliedAtStep,
  IReadOnlyList<ProposedEventRecord> ProposedEvents = null!,
  IReadOnlyList<ReplacementDecision> ReplacementDecisions = null!)
{
  public IReadOnlyList<ProposedEventRecord> ProposedEvents { get; init; } = ProposedEvents ?? [];

  public IReadOnlyList<ReplacementDecision> ReplacementDecisions { get; init; } = ReplacementDecisions ?? [];
}

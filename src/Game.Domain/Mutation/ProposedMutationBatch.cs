namespace Game.Domain.Mutation;

public sealed record ProposedMutationBatch(
  string BatchId,
  int CommandSeq,
  int? ParentEventSeq,
  string Phase,
  string SourceRef,
  string Reason,
  IReadOnlyList<StateMutation> Mutations,
  string PreStateHash,
  string? ReplacementPolicyRef,
  string? FizzlePolicyRef);

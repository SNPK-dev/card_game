namespace Game.Domain.Mutation;

public sealed record StateMutation(
  string MutationId,
  string MutationType,
  string SourceRef,
  string? TargetRef,
  IReadOnlyDictionary<string, int> Payload,
  string Phase,
  int CausedByCommandSeq,
  int? CausedByEventSeq,
  string OrderingKey,
  IReadOnlyDictionary<string, string>? Metadata = null);

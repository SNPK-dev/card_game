namespace Game.Domain.Randomness;

public sealed record RngRollRecord(
  string RngRollId,
  RngStreamKind Stream,
  ulong PreRngState,
  ulong PostRngState,
  int CandidateCount,
  int SelectedIndex,
  string Reason,
  int CommandSeq,
  long? EventSeq);


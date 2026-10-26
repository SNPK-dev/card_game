namespace Game.Shared.Mechanics;

public sealed record StatusTransition(
  MechanicStatus? From,
  MechanicStatus To,
  DateTimeOffset ChangedAt,
  string ChangedBy,
  string Reason,
  IReadOnlyList<string> EvidenceRefs);

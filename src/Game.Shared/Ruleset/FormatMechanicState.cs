namespace Game.Shared.Ruleset;

public sealed record FormatMechanicState(
  string MechanicId,
  string RulesetProfileId,
  FormatMechanicStateKind State,
  string? Reason,
  string? EffectiveFrom,
  string? EffectiveUntil);

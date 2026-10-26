namespace Game.Shared.Ruleset;

public sealed record LegacyPolicy(
  bool AllowDeprecatedMechanicsForReplay,
  bool AllowDeprecatedMechanicsForNewCards,
  bool AllowDisabledMechanicsForReplay,
  bool AllowDisabledMechanicsForNewCards,
  bool ReplayProfilePinningRequired);

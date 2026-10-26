namespace Game.Domain.State;

public sealed record PlayerAura(
  string AuraId,
  string SourceEntityInstanceId,
  string SourceCardInstanceId,
  string SourceCardVersion,
  string ControllerPlayerId,
  string EffectId,
  int Priority);

namespace Game.Domain.State;

public sealed record PlayerSecret(
  string SecretId,
  string SourceCardInstanceId,
  string SourceCardVersion,
  string ControllerPlayerId,
  string RevealEventType,
  string EffectId,
  bool IsRevealed);

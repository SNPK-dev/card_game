namespace Game.Domain.State;

public sealed record HeroPowerState(
  string SourceCardInstanceId,
  int Cost,
  string ActivationTiming);

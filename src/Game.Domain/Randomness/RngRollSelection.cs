namespace Game.Domain.Randomness;

public sealed record RngRollSelection<T>(
  T Selected,
  RngRollRecord Roll);


namespace Game.Domain.Randomness;

public sealed class GameplayRandomStream(ulong seed) : DeterministicRandomStream(seed), IGameplayRandomStream;

namespace Game.Domain.Randomness;

public interface IDeterministicRandomStream
{
  ulong Seed { get; }

  ulong CurrentState { get; }

  uint NextUInt32();
}

namespace Game.Domain.Randomness;

public class DeterministicRandomStream : IDeterministicRandomStream
{
  private ulong _state;

  public DeterministicRandomStream(ulong seed)
  {
    Seed = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
    _state = Seed;
  }

  public ulong Seed { get; }

  public ulong CurrentState => _state;

  public uint NextUInt32()
  {
    _state ^= _state << 13;
    _state ^= _state >> 7;
    _state ^= _state << 17;

    return (uint)(_state >> 32);
  }
}

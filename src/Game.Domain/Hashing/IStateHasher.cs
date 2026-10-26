using Game.Domain.State;

namespace Game.Domain.Hashing;

public interface IStateHasher
{
  string HashGameplayState(IGameState state);

  string HashCanonicalBytes(byte[] canonicalBytes);
}

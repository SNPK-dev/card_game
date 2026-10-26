using System.Security.Cryptography;
using Game.Domain.Serialization;
using Game.Domain.State;

namespace Game.Domain.Hashing;

public sealed class StateHasher(ICanonicalSerializer serializer) : IStateHasher
{
  public string HashGameplayState(IGameState state)
  {
    ArgumentNullException.ThrowIfNull(state);
    return HashCanonicalBytes(serializer.SerializeToCanonicalBytes(state.GetGameplaySnapshot()));
  }

  public string HashCanonicalBytes(byte[] canonicalBytes)
  {
    ArgumentNullException.ThrowIfNull(canonicalBytes);
    var hash = SHA256.HashData(canonicalBytes);
    return Convert.ToHexString(hash);
  }
}

namespace Game.Domain.Serialization;

public interface ICanonicalSerializer
{
  byte[] SerializeToCanonicalBytes<T>(T value);
}

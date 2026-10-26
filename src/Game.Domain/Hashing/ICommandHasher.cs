using Game.Domain.Commands;

namespace Game.Domain.Hashing;

public interface ICommandHasher
{
  string HashCommand(DeterministicCommandPayload command);
}

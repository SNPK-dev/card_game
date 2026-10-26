using Game.Shared.Events;

namespace Game.Domain.Hashing;

public interface ICommittedEventLogHasher
{
  string HashGameplayEvents(IEnumerable<CommittedEventRecord> events);
}

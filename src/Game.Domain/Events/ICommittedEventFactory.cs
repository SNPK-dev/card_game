using Game.Domain.Mutation;
using Game.Shared.Events;

namespace Game.Domain.Events;

public interface ICommittedEventFactory
{
  IReadOnlyList<CommittedEventRecord> CreateCommittedEvents(
    AppliedMutationBatch appliedBatch,
    long firstEventSeq);
}

using Game.Domain.Events;
using Game.Domain.Mutation;
using Game.Domain.State;
using Game.Shared.Events;

namespace Game.Application.Execution;

public sealed record MutationBatchExecutionResult(
  IGameState State,
  AppliedMutationBatch AppliedBatch,
  IReadOnlyList<CommittedEventRecord> CommittedEvents,
  IReadOnlyList<ProcessedEvent> ProcessedEvents);

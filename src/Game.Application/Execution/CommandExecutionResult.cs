using Game.Domain.State;
using Game.Shared.Events;

namespace Game.Application.Execution;

public sealed record CommandExecutionResult(IGameState State, IReadOnlyList<CommittedEventRecord> Events);

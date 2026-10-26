using Game.Domain.State;
using Game.Shared.Events;

namespace Game.Application.Replay;

public sealed record CommandLogReplayResult(
  IGameState State,
  IReadOnlyList<CommittedEventRecord> DerivedEvents);

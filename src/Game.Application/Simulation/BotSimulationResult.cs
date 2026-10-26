using Game.Application.Replay;
using Game.Domain.Replay;
using Game.Domain.Randomness;
using Game.Domain.State;
using Game.Shared.Events;

namespace Game.Application.Simulation;

public sealed record BotSimulationResult(
  string SimulationId,
  IGameState InitialState,
  IGameState SetupState,
  IGameState FinalState,
  CanonicalCommandLog CommandLog,
  string InitialStateHash,
  string SetupStateHash,
  string FinalStateHash,
  GameTerminalState TerminalState,
  IReadOnlyList<CommittedEventRecord> DerivedEvents,
  ReplayVerificationResult ReplayVerification,
  int ReplayVerificationRuns = 1,
  IReadOnlyList<RngRollRecord>? BotDecisionRolls = null);

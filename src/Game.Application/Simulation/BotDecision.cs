using Game.Domain.Commands;
using Game.Domain.Randomness;

namespace Game.Application.Simulation;

public sealed record BotDecision(
  IGameCommand Command,
  RngRollRecord? RngRoll = null);

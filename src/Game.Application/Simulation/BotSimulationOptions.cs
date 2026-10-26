namespace Game.Application.Simulation;

public sealed record BotSimulationOptions(
  int OpeningHandSize = 1,
  int MaxCommands = 20,
  ulong BotSeed = 0xB07DEC1510A11CEUL);

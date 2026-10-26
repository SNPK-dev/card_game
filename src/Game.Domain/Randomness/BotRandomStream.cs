namespace Game.Domain.Randomness;

public sealed class BotRandomStream(ulong seed) : DeterministicRandomStream(seed ^ 0xC2B2AE3D27D4EB4FUL);

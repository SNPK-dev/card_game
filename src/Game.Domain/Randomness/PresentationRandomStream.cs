namespace Game.Domain.Randomness;

public sealed class PresentationRandomStream(ulong seed) : DeterministicRandomStream(seed ^ 0xA5A5A5A5A5A5A5A5UL), IPresentationRandomStream;

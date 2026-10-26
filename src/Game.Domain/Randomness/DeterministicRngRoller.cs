namespace Game.Domain.Randomness;

public sealed class DeterministicRngRoller(IDeterministicRandomStream stream) : IRngRoller
{
  public RngRollSelection<T> SelectOne<T>(
    IReadOnlyList<T> candidates,
    RngStreamKind streamKind,
    string reason,
    int commandSeq,
    string rollScope)
  {
    ArgumentNullException.ThrowIfNull(candidates);
    if (candidates.Count == 0)
    {
      throw new ArgumentException("At least one RNG candidate is required.", nameof(candidates));
    }

    if (commandSeq <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(commandSeq), "Command sequence must be positive.");
    }

    if (string.IsNullOrWhiteSpace(reason))
    {
      throw new ArgumentException("RNG roll reason is required.", nameof(reason));
    }

    if (string.IsNullOrWhiteSpace(rollScope))
    {
      throw new ArgumentException("RNG roll scope is required.", nameof(rollScope));
    }

    var preState = stream.CurrentState;
    var value = stream.NextUInt32();
    var postState = stream.CurrentState;
    var selectedIndex = (int)(value % (uint)candidates.Count);
    var roll = new RngRollRecord(
      $"rng:{commandSeq}:{rollScope}",
      streamKind,
      preState,
      postState,
      candidates.Count,
      selectedIndex,
      reason,
      commandSeq,
      null);

    return new RngRollSelection<T>(candidates[selectedIndex], roll);
  }
}

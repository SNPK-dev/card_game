namespace Game.Domain.Randomness;

public interface IRngRoller
{
  RngRollSelection<T> SelectOne<T>(
    IReadOnlyList<T> candidates,
    RngStreamKind stream,
    string reason,
    int commandSeq,
    string rollScope);
}

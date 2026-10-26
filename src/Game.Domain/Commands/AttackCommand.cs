namespace Game.Domain.Commands;

public sealed record AttackCommand(
  string PlayerId,
  string AttackerEntityInstanceId,
  string TargetPlayerId,
  string TargetEntityInstanceId) : IGameCommand
{
  public const string CommandKind = "ATTACK";

  public string Kind => CommandKind;

  public IReadOnlyDictionary<string, int> Parameters { get; } =
    new SortedDictionary<string, int>(StringComparer.Ordinal);
}

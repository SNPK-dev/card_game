namespace Game.Domain.Commands;

public sealed record UseHeroPowerCommand(
  string PlayerId,
  string SourceCardInstanceId,
  int ManaCost,
  string TargetPlayerId = "",
  string TargetEntityInstanceId = "") : IGameCommand
{
  public const string CommandKind = "USE_HERO_POWER";

  public string Kind => CommandKind;

  public IReadOnlyDictionary<string, int> Parameters =>
    new SortedDictionary<string, int>(StringComparer.Ordinal)
    {
      ["manaCost"] = ManaCost
    };
}

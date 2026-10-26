namespace Game.Domain.Commands;

public sealed record ActivateLocationCommand(
  string PlayerId,
  string LocationEntityInstanceId,
  string SourceCardInstanceId,
  string TargetPlayerId = "",
  string TargetEntityInstanceId = "") : IGameCommand
{
  public const string CommandKind = "ACTIVATE_LOCATION";

  public string Kind => CommandKind;

  public IReadOnlyDictionary<string, int> Parameters { get; } =
    new SortedDictionary<string, int>(StringComparer.Ordinal);
}

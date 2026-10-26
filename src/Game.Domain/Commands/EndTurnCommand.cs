using System.Text.Json.Serialization;

namespace Game.Domain.Commands;

public sealed record EndTurnCommand(
  string PlayerId,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  int? MaxHandSize = null) : IGameCommand
{
  public const string CommandKind = "END_TURN";

  public string Kind => CommandKind;

  public IReadOnlyDictionary<string, int> Parameters =>
    new SortedDictionary<string, int>(StringComparer.Ordinal)
      .WithOptional("maxHandSize", MaxHandSize);
}

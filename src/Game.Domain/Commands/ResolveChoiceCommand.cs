using Game.Shared.Primitives;

namespace Game.Domain.Commands;

public sealed class ResolveChoiceCommand : IGameCommand
{
  public const string CommandKind = "RESOLVE_CHOICE";

  public ResolveChoiceCommand(
    string playerId,
    string choiceId,
    string selectedOptionId,
    string cardInstanceId,
    string cardVersion,
    CardType cardType,
    IReadOnlyList<string>? selectedOptionIds = null)
  {
    PlayerId = playerId;
    ChoiceId = choiceId;
    SelectedOptionId = selectedOptionId;
    SelectedOptionIds = NormalizeSelectedOptionIds(selectedOptionIds, selectedOptionId);
    CardInstanceId = cardInstanceId;
    CardVersion = cardVersion;
    CardType = cardType;
  }

  public string PlayerId { get; init; }

  public string ChoiceId { get; init; }

  public string SelectedOptionId { get; init; }

  public IReadOnlyList<string> SelectedOptionIds { get; init; }

  public string CardInstanceId { get; init; }

  public string CardVersion { get; init; }

  public CardType CardType { get; init; }

  public string Kind => CommandKind;

  public IReadOnlyDictionary<string, int> Parameters =>
    new SortedDictionary<string, int>(StringComparer.Ordinal);

  private static IReadOnlyList<string> NormalizeSelectedOptionIds(
    IReadOnlyList<string>? selectedOptionIds,
    string selectedOptionId)
  {
    IEnumerable<string> source = selectedOptionIds is { Count: > 0 }
      ? selectedOptionIds
      : [selectedOptionId];

    var values = source
      .Where(value => !string.IsNullOrWhiteSpace(value))
      .Distinct(StringComparer.Ordinal)
      .Order(StringComparer.Ordinal)
      .ToArray();
    return values.Length > 0 ? values : [selectedOptionId];
  }
}

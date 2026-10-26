namespace Game.Domain.Commands;

public sealed class DeterministicCommandPayload
{
  public DeterministicCommandPayload(
    int commandSeq,
    string? playerId,
    string commandType,
    IReadOnlyDictionary<string, int>? payload = null,
    IReadOnlyDictionary<string, string>? refs = null,
    string? expectedPreStateHash = null,
    CommandCategory commandCategory = CommandCategory.Player,
    bool debugReplayFlag = false)
  {
    CommandSeq = commandSeq;
    PlayerId = playerId;
    CommandType = commandType;
    var sortedPayload = new SortedDictionary<string, int>(StringComparer.Ordinal);
    foreach (var pair in payload ?? EmptyPayload.Value)
    {
      sortedPayload[pair.Key] = pair.Value;
    }

    Payload = sortedPayload;
    var sortedRefs = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (var pair in refs ?? EmptyRefs.Value)
    {
      sortedRefs[pair.Key] = pair.Value;
    }

    Refs = sortedRefs;
    ExpectedPreStateHash = expectedPreStateHash;
    CommandCategory = commandCategory;
    DebugReplayFlag = debugReplayFlag;
  }

  public int CommandSeq { get; }

  public string? PlayerId { get; }

  public string CommandType { get; }

  public IReadOnlyDictionary<string, int> Payload { get; }

  public IReadOnlyDictionary<string, string> Refs { get; }

  public string? ExpectedPreStateHash { get; }

  public CommandCategory CommandCategory { get; }

  public bool DebugReplayFlag { get; }

  public IGameCommand ToCommand()
  {
    if (string.IsNullOrWhiteSpace(CommandType))
    {
      throw new InvalidOperationException("CommandType must be set before replay.");
    }

    return CommandType switch
    {
      ActivateLocationCommand.CommandKind => new ActivateLocationCommand(
        RequiredPlayerId(ActivateLocationCommand.CommandKind),
        RequiredRef("locationEntityInstanceId", ActivateLocationCommand.CommandKind),
        RequiredRef("sourceCardInstanceId", ActivateLocationCommand.CommandKind),
        OptionalRef("targetPlayerId"),
        OptionalRef("targetEntityInstanceId")),
      PlayCardCommand.CommandKind => new PlayCardCommand(
        RequiredPlayerId(PlayCardCommand.CommandKind),
        RequiredRef("cardInstanceId", PlayCardCommand.CommandKind),
        OptionalRef("entityInstanceId"),
        OptionalPayload("attack"),
        OptionalPayload("health"),
        OptionalPayload("manaCost"),
        OptionalNullablePayload("maxBoardSize"),
        KeywordRefs("entityKeyword:"),
        OptionalPayload("costReduction"),
        OptionalPayload("durability"),
        OptionalPayload("armorGain"),
        OptionalNullablePayload("heroPowerCost"),
        OptionalRef("heroPowerActivationTiming"),
        OptionalRef("targetPlayerId"),
        OptionalRef("targetEntityInstanceId")),
      AttackCommand.CommandKind => new AttackCommand(
        RequiredPlayerId(AttackCommand.CommandKind),
        RequiredRef("attackerEntityInstanceId", AttackCommand.CommandKind),
        RequiredRef("targetPlayerId", AttackCommand.CommandKind),
        RequiredRef("targetEntityInstanceId", AttackCommand.CommandKind)),
      ConcedeCommand.CommandKind => new ConcedeCommand(
        RequiredPlayerId(ConcedeCommand.CommandKind)),
      EndTurnCommand.CommandKind => new EndTurnCommand(
        RequiredPlayerId(EndTurnCommand.CommandKind),
        OptionalNullablePayload("maxHandSize")),
      UseHeroPowerCommand.CommandKind => new UseHeroPowerCommand(
        RequiredPlayerId(UseHeroPowerCommand.CommandKind),
        RequiredRef("sourceCardInstanceId", UseHeroPowerCommand.CommandKind),
        RequiredPayload("manaCost", UseHeroPowerCommand.CommandKind),
        OptionalRef("targetPlayerId"),
        OptionalRef("targetEntityInstanceId")),
      ResolveChoiceCommand.CommandKind => new ResolveChoiceCommand(
        RequiredPlayerId(ResolveChoiceCommand.CommandKind),
        RequiredRef("choiceId", ResolveChoiceCommand.CommandKind),
        RequiredRef("selectedOptionId", ResolveChoiceCommand.CommandKind),
        RequiredRef("cardInstanceId", ResolveChoiceCommand.CommandKind),
        RequiredRef("cardVersion", ResolveChoiceCommand.CommandKind),
        RequiredCardType("cardType", ResolveChoiceCommand.CommandKind),
        OptionIdRefs()),
      _ => new NoOpCommand(CommandType, Payload)
    };
  }

  private string RequiredPlayerId(string commandType) =>
    string.IsNullOrWhiteSpace(PlayerId)
      ? throw new InvalidOperationException($"{commandType} replay payload requires player id.")
      : PlayerId;

  private string RequiredRef(string key, string commandType) =>
    Refs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
      ? value
      : throw new InvalidOperationException($"{commandType} replay payload requires ref '{key}'.");

  private string OptionalRef(string key) =>
    Refs.TryGetValue(key, out var value)
      ? value
      : string.Empty;

  private int RequiredPayload(string key, string commandType) =>
    Payload.TryGetValue(key, out var value)
      ? value
      : throw new InvalidOperationException($"{commandType} replay payload requires payload '{key}'.");

  private int OptionalPayload(string key) =>
    Payload.TryGetValue(key, out var value)
      ? value
      : 0;

  private int? OptionalNullablePayload(string key) =>
    Payload.TryGetValue(key, out var value)
      ? value
      : null;

  private IReadOnlyList<string> KeywordRefs(string prefix) =>
    Refs
      .Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(pair.Value))
      .OrderBy(pair => pair.Key, StringComparer.Ordinal)
      .Select(pair => pair.Value)
      .Distinct(StringComparer.Ordinal)
      .ToArray();

  private IReadOnlyList<string> OptionIdRefs()
  {
    if (Refs.TryGetValue("selectedOptionIds", out var selectedOptionIds)
      && !string.IsNullOrWhiteSpace(selectedOptionIds))
    {
      return selectedOptionIds
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
    }

    return [RequiredRef("selectedOptionId", ResolveChoiceCommand.CommandKind)];
  }

  private Game.Shared.Primitives.CardType RequiredCardType(string key, string commandType)
  {
    var value = RequiredRef(key, commandType);
    return value switch
    {
      "MINION_CARD" => Game.Shared.Primitives.CardType.MinionCard,
      "SPELL_CARD" => Game.Shared.Primitives.CardType.SpellCard,
      "WEAPON_CARD" => Game.Shared.Primitives.CardType.WeaponCard,
      "LOCATION_CARD" => Game.Shared.Primitives.CardType.LocationCard,
      "HERO_CARD" => Game.Shared.Primitives.CardType.HeroCard,
      "HERO_POWER_CARD" => Game.Shared.Primitives.CardType.HeroPowerCard,
      "TOKEN_CARD" => Game.Shared.Primitives.CardType.TokenCard,
      _ => throw new InvalidOperationException($"{commandType} replay payload has unsupported cardType '{value}'.")
    };
  }

  private static class EmptyPayload
  {
    internal static readonly IReadOnlyDictionary<string, int> Value =
      new SortedDictionary<string, int>(StringComparer.Ordinal);
  }

  private static class EmptyRefs
  {
    internal static readonly IReadOnlyDictionary<string, string> Value =
      new SortedDictionary<string, string>(StringComparer.Ordinal);
  }
}

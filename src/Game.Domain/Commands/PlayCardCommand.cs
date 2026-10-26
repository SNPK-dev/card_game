using System.Text.Json.Serialization;

namespace Game.Domain.Commands;

public sealed record PlayCardCommand : IGameCommand
{
  public const string CommandKind = "PLAY_CARD";

  public PlayCardCommand(
    string PlayerId,
    string CardInstanceId,
    string EntityInstanceId = "",
    int Attack = 0,
    int Health = 0,
    int ManaCost = 0,
    int? MaxBoardSize = null,
    IReadOnlyList<string>? Keywords = null,
    int CostReduction = 0,
    int Durability = 0,
    int ArmorGain = 0,
    int? HeroPowerCost = null,
    string HeroPowerActivationTiming = "",
    string TargetPlayerId = "",
    string TargetEntityInstanceId = "")
  {
    this.PlayerId = PlayerId;
    this.CardInstanceId = CardInstanceId;
    this.EntityInstanceId = EntityInstanceId ?? string.Empty;
    this.Attack = Attack;
    this.Health = Health;
    this.ManaCost = ManaCost;
    this.MaxBoardSize = MaxBoardSize;
    this.Keywords = NormalizeKeywords(Keywords);
    this.CostReduction = CostReduction;
    this.Durability = Durability;
    this.ArmorGain = ArmorGain;
    this.HeroPowerCost = HeroPowerCost;
    this.HeroPowerActivationTiming = HeroPowerActivationTiming ?? string.Empty;
    this.TargetPlayerId=TargetPlayerId??string.Empty;
    this.TargetEntityInstanceId=TargetEntityInstanceId??string.Empty;
  }

  public string PlayerId { get; init; }

  public string CardInstanceId { get; init; }

  public string EntityInstanceId { get; init; }

  public int Attack { get; init; }

  public int Health { get; init; }

  public int ManaCost { get; init; }

  public int CostReduction { get; init; }

  public int Durability { get; init; }

  public int ArmorGain { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? HeroPowerCost { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public string HeroPowerActivationTiming { get; init; }
  public string TargetPlayerId { get; init; }
  public string TargetEntityInstanceId { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? MaxBoardSize { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public IReadOnlyList<string>? Keywords { get; init; }

  public string Kind => CommandKind;

  public IReadOnlyDictionary<string, int> Parameters =>
    new SortedDictionary<string, int>(StringComparer.Ordinal)
    {
      ["attack"] = Attack,
      ["health"] = Health,
      ["manaCost"] = ManaCost
    }
      .WithOptional("maxBoardSize", MaxBoardSize)
      .WithOptionalPositive("costReduction", CostReduction)
      .WithOptionalPositive("durability", Durability)
      .WithOptionalPositive("armorGain", ArmorGain)
      .WithOptional("heroPowerCost", HeroPowerCost);

  private static IReadOnlyList<string>? NormalizeKeywords(IReadOnlyList<string>? keywords)
  {
    var normalized = (keywords ?? [])
      .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
      .Distinct(StringComparer.Ordinal)
      .Order(StringComparer.Ordinal)
      .ToArray();

    return normalized.Length == 0 ? null : normalized;
  }
}

internal static class CommandParameterExtensions
{
  public static IReadOnlyDictionary<string, int> WithOptional(
    this SortedDictionary<string, int> values,
    string key,
    int? value)
  {
    if (value.HasValue)
    {
      values[key] = value.Value;
    }

    return values;
  }

  public static IReadOnlyDictionary<string, int> WithOptional(
    this IReadOnlyDictionary<string, int> values,
    string key,
    int? value)
  {
    var next = new SortedDictionary<string, int>(StringComparer.Ordinal);
    foreach (var pair in values)
    {
      next[pair.Key] = pair.Value;
    }

    if (value.HasValue)
    {
      next[key] = value.Value;
    }

    return next;
  }

  public static IReadOnlyDictionary<string, int> WithOptionalPositive(
    this IReadOnlyDictionary<string, int> values,
    string key,
    int value)
  {
    var next = new SortedDictionary<string, int>(StringComparer.Ordinal);
    foreach (var pair in values)
    {
      next[pair.Key] = pair.Value;
    }

    if (value > 0)
    {
      next[key] = value;
    }

    return next;
  }
}

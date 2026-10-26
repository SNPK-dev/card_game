using System.Text.Json.Serialization;
using Game.Shared.Primitives;

namespace Game.Domain.State;

public sealed record EntityInstance
{
  public EntityInstance(
    string EntityInstanceId,
    string SourceCardInstanceId,
    string ControllerPlayerId,
    EntityType EntityType,
    int Attack,
    int Health,
    int Damage,
    IReadOnlyList<string>? Keywords = null)
  {
    this.EntityInstanceId = EntityInstanceId;
    this.SourceCardInstanceId = SourceCardInstanceId;
    this.ControllerPlayerId = ControllerPlayerId;
    this.EntityType = EntityType;
    this.Attack = Attack;
    this.Health = Health;
    this.Damage = Damage;
    this.Keywords = NormalizeKeywords(Keywords);
  }

  public string EntityInstanceId { get; init; }

  public string SourceCardInstanceId { get; init; }

  public string ControllerPlayerId { get; init; }

  public EntityType EntityType { get; init; }

  public int Attack { get; init; }

  public int Health { get; init; }

  public int Damage { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public IReadOnlyList<string>? Keywords { get; init; }

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

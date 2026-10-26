namespace Game.Domain.State;

public sealed record PlayerModifier(
  string ModifierId,
  string ModifierType,
  int Amount,
  string Duration,
  int Priority,
  string SourceRef);

public static class PlayerModifierTypes
{
  public const string CostModifier = "COST_MODIFIER";
  public const string DamagePrevention = "DAMAGE_PREVENTION";
  public const string BoardAttackModifier = "BOARD_ATTACK_MODIFIER";
  public const string BoardHealthModifier = "BOARD_HEALTH_MODIFIER";
  public const string BoardKeywordModifier = "BOARD_KEYWORD_MODIFIER";
  public const string HandZoneModifier = "HAND_ZONE_MODIFIER";
}

public static class PlayerModifierDurations
{
  public const string NextPlay = "NEXT_PLAY";
  public const string NextDamage = "NEXT_DAMAGE";
  public const string RestOfTurn = "REST_OF_TURN";
  public const string Permanent = "PERMANENT";
  public const string WhileSourceActive = "WHILE_SOURCE_ACTIVE";
}

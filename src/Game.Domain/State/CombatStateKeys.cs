namespace Game.Domain.State;

public static class CombatStateKeys
{
  public static string AttackCounterKey(
    int turnNumber,
    string playerId,
    string attackerEntityInstanceId) =>
    $"attack:{turnNumber}:{playerId}:{attackerEntityInstanceId}";

  public static string SummonCounterKey(
    string playerId,
    string entityInstanceId) =>
    $"summoned:{playerId}:{entityInstanceId}";
}

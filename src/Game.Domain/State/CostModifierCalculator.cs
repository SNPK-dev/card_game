namespace Game.Domain.State;

public static class CostModifierCalculator
{
  public static int CalculateReduction(
    PlayerState player,
    int baseManaCost,
    int? maxReduction = null)
  {
    ArgumentNullException.ThrowIfNull(player);
    if (baseManaCost <= 0)
    {
      return 0;
    }

    var total = ApplicableCostModifiers(player).Sum(modifier => modifier.Amount);
    var cappedByCost = Math.Min(baseManaCost, total);
    return Math.Min(cappedByCost, maxReduction ?? cappedByCost);
  }

  public static IReadOnlyList<PlayerModifier> ApplicableCostModifiers(PlayerState player)
  {
    ArgumentNullException.ThrowIfNull(player);

    return player.Modifiers
      .Where(modifier =>
        StringComparer.Ordinal.Equals(modifier.ModifierType, PlayerModifierTypes.CostModifier)
        && IsApplicableCostModifierDuration(modifier.Duration)
        && modifier.Amount > 0)
      .OrderByDescending(modifier => modifier.Priority)
      .ThenBy(modifier => modifier.ModifierId, StringComparer.Ordinal)
      .ToArray();
  }

  public static IReadOnlyList<PlayerModifier> ConsumedOnPlayCostModifiers(PlayerState player)
  {
    ArgumentNullException.ThrowIfNull(player);

    return ApplicableCostModifiers(player)
      .Where(modifier => StringComparer.Ordinal.Equals(modifier.Duration, PlayerModifierDurations.NextPlay))
      .ToArray();
  }

  public static IReadOnlyList<PlayerModifier> ExpireAtEndOfTurnCostModifiers(PlayerState player)
  {
    ArgumentNullException.ThrowIfNull(player);

    return ApplicableCostModifiers(player)
      .Where(modifier => StringComparer.Ordinal.Equals(modifier.Duration, PlayerModifierDurations.RestOfTurn))
      .ToArray();
  }

  private static bool IsApplicableCostModifierDuration(string duration) =>
    StringComparer.Ordinal.Equals(duration, PlayerModifierDurations.NextPlay)
    || StringComparer.Ordinal.Equals(duration, PlayerModifierDurations.RestOfTurn);
}

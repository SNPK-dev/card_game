namespace Game.Domain.State;

public static class ExtraTurnSchedule
{
  private const string QueuePrefix = "extra-turn:queue:";
  private const string UsedPrefix = "extra-turn:used:";

  public static string QueueKey(int commandSeq, string effectId, string actionId, string playerId) =>
    $"{QueuePrefix}{commandSeq:000000}:{Normalize(effectId)}:{Normalize(actionId)}:{Normalize(playerId)}";

  public static string UsedKey(string playerId) => $"{UsedPrefix}{Normalize(playerId)}";

  public static bool HasOncePerGameUse(IGameState state, string playerId) =>
    state.Counters.TryGetValue(UsedKey(playerId), out var value) && value > 0;

  public static bool TryGetNext(
    IReadOnlyDictionary<string, int> counters,
    out string playerId,
    out string queueKey)
  {
    foreach (var entry in counters
      .Where(entry => entry.Value > 0 && entry.Key.StartsWith(QueuePrefix, StringComparison.Ordinal))
      .OrderBy(entry => entry.Key, StringComparer.Ordinal))
    {
      var index = entry.Key.LastIndexOf(':');
      if (index < 0 || index == entry.Key.Length - 1)
      {
        continue;
      }

      playerId = entry.Key[(index + 1)..];
      queueKey = entry.Key;
      return true;
    }

    playerId = string.Empty;
    queueKey = string.Empty;
    return false;
  }

  private static string Normalize(string value) =>
    string.IsNullOrWhiteSpace(value)
      ? "missing"
      : value.Replace(':', '_');
}

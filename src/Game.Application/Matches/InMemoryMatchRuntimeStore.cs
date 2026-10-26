namespace Game.Application.Matches;

public sealed class InMemoryMatchRuntimeStore : IMatchRuntimeStore
{
  private readonly object syncRoot = new();
  private readonly SortedDictionary<string, MatchRuntimeRecord> matches = new(StringComparer.Ordinal);

  public bool TryAdd(MatchRuntimeRecord record)
  {
    ArgumentNullException.ThrowIfNull(record);
    lock (syncRoot)
    {
      if (matches.ContainsKey(record.MatchId))
      {
        return false;
      }

      matches[record.MatchId] = record;
      return true;
    }
  }

  public MatchRuntimeRecord? Find(string matchId)
  {
    lock (syncRoot)
    {
      return matches.TryGetValue(matchId, out var record) ? record : null;
    }
  }

  public void Update(MatchRuntimeRecord record)
  {
    ArgumentNullException.ThrowIfNull(record);
    lock (syncRoot)
    {
      matches[record.MatchId] = record;
    }
  }
}

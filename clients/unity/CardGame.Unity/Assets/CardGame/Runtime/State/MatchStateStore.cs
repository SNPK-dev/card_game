using System;
using System.Collections.Generic;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.State
{
  public sealed class MatchStateStore
  {
    public MatchStateView Current { get; private set; }
    public string StateHash => Current?.StateHash ?? string.Empty;
    public int CommandCount => Current?.CommandCount ?? 0;
    public long NextEventSeq => Current?.NextEventSeq ?? 0;
    public string ViewerPlayerId => Current?.ViewerPlayerId ?? string.Empty;
    public long LastSeenEventSeq { get; private set; }

    private readonly HashSet<long> seenEvents = new();

    public void ReplaceFromServer(MatchStateView state)
    {
      Current = state ?? throw new ArgumentNullException(nameof(state));
      LastSeenEventSeq = Math.Max(LastSeenEventSeq, state.NextEventSeq - 1);
    }

    public void ReplaceAfterReconnect(MatchStateView state, long nextEventSeq)
    {
      Current = state ?? throw new ArgumentNullException(nameof(state));
      LastSeenEventSeq = Math.Max(0, nextEventSeq - 1);
      seenEvents.Clear();
    }

    public bool TryAcceptEvent(PublicMatchEventRecord evt)
    {
      if (evt == null || seenEvents.Contains(evt.EventSeq))
      {
        return false;
      }

      seenEvents.Add(evt.EventSeq);
      LastSeenEventSeq = Math.Max(LastSeenEventSeq, evt.EventSeq);
      return true;
    }
  }
}

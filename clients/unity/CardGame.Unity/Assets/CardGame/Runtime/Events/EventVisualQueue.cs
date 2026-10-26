using System;
using System.Collections.Generic;
using System.Linq;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.Events
{
  public sealed class EventVisualQueue
  {
    private readonly EventVisualDispatcher dispatcher;
    private readonly SortedDictionary<long, PublicMatchEventRecord> pending = new();
    private readonly HashSet<long> seen = new();

    public EventVisualQueue(EventVisualDispatcher dispatcher)
    {
      this.dispatcher = dispatcher;
    }

    public EventVisualMessage Current { get; private set; }
    public bool RefreshRequested { get; private set; }

    public bool Enqueue(PublicMatchEventRecord evt)
    {
      if (evt == null || seen.Contains(evt.EventSeq))
      {
        return false;
      }

      seen.Add(evt.EventSeq);
      var message = dispatcher.Dispatch(evt);
      if (message.Blocking)
      {
        pending.Clear();
        Current = message;
        RefreshRequested |= message.RefreshState;
        return true;
      }

      pending[evt.EventSeq] = evt;
      return true;
    }

    public EventVisualMessage Advance()
    {
      if (Current?.Blocking == true)
      {
        return Current;
      }

      if (pending.Count == 0)
      {
        Current = null;
        return null;
      }

      var first = pending.First();
      pending.Remove(first.Key);
      Current = dispatcher.Dispatch(first.Value);
      RefreshRequested |= Current.RefreshState;
      return Current;
    }

    public bool CompleteCurrent()
    {
      if (Current?.Blocking == true)
      {
        return RefreshRequested;
      }

      Current = null;
      return RefreshRequested;
    }

    public void ClearRefreshRequest()
    {
      RefreshRequested = false;
    }
  }

  public sealed class EventVisualTiming
  {
    private static readonly Dictionary<string, float> Timings = new(StringComparer.OrdinalIgnoreCase)
    {
      ["turn"] = 0.25f,
      ["card"] = 0.40f,
      ["zone"] = 0.30f,
      ["combat"] = 0.30f,
      ["board"] = 0.35f,
      ["choice"] = 0.25f,
      ["terminal"] = 0.50f
    };

    public float FallbackSeconds { get; set; } = 0.25f;

    public float DurationSeconds(EventVisualMessage message)
    {
      if (message == null)
      {
        return 0f;
      }

      if (message.Blocking)
      {
        return float.PositiveInfinity;
      }

      return Timings.TryGetValue(message.Category, out var duration) ? duration : FallbackSeconds;
    }
  }
}

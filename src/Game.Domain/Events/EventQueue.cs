using Game.Shared.Events;

namespace Game.Domain.Events;

public sealed class EventQueue
{
  private readonly Queue<QueuedEvent> _events = new();
  private readonly EventQueueOptions _options;
  private int _nextEventSeq;

  public EventQueue(EventQueueOptions? options = null, int nextEventSeq = 1)
  {
    if (nextEventSeq < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(nextEventSeq), "Next event sequence must be positive.");
    }

    _options = options ?? EventQueueOptions.MvpDefaults;
    if (_options.MaxEventDepth < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(options), "Max event depth cannot be negative.");
    }

    if (_options.MaxQueuedEvents < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(options), "Max queued events must be positive.");
    }

    _nextEventSeq = nextEventSeq;
  }

  public int Count => _events.Count;

  public void EnqueueRoot(QueuedEvent queuedEvent)
  {
    ArgumentNullException.ThrowIfNull(queuedEvent);
    EnqueueValidated(queuedEvent);
  }

  public void EnqueueChildren(IEnumerable<QueuedEvent> childEvents)
  {
    ArgumentNullException.ThrowIfNull(childEvents);

    var orderedChildren = childEvents
      .Select((queuedEvent, index) => new ChildEvent(queuedEvent, index))
      .OrderBy(child => child.Event.CanonicalOrderingKeys, CanonicalOrderingKeyComparer.Instance)
      .ThenBy(child => child.InputOrder)
      .Select(child => child.Event)
      .ToArray();

    EnsureCapacityFor(orderedChildren.Length);
    foreach (var queuedEvent in orderedChildren)
    {
      ValidateEvent(queuedEvent);
    }

    foreach (var queuedEvent in orderedChildren)
    {
      _events.Enqueue(queuedEvent);
    }
  }

  public ProcessedEvent Dequeue()
  {
    if (_events.Count == 0)
    {
      throw new InvalidOperationException("Cannot dequeue from an empty event queue.");
    }

    return new ProcessedEvent(_nextEventSeq++, _events.Dequeue());
  }

  public IReadOnlyList<ProcessedEvent> Drain()
  {
    var processed = new List<ProcessedEvent>(_events.Count);
    while (_events.Count > 0)
    {
      processed.Add(Dequeue());
    }

    return processed;
  }

  private void EnqueueValidated(QueuedEvent queuedEvent)
  {
    EnsureCapacityFor(1);
    ValidateEvent(queuedEvent);
    _events.Enqueue(queuedEvent);
  }

  private void EnsureCapacityFor(int eventCount)
  {
    if (eventCount < 0 || _events.Count + eventCount > _options.MaxQueuedEvents)
    {
      throw new InvalidOperationException("Event queue maximum event count would be exceeded.");
    }
  }

  private void ValidateEvent(QueuedEvent queuedEvent)
  {
    if (queuedEvent.Category == EventCategory.Presentation)
    {
      throw new InvalidOperationException("Presentation events cannot be enqueued into the gameplay event queue.");
    }

    if (queuedEvent.Depth > _options.MaxEventDepth)
    {
      throw new InvalidOperationException("Event queue maximum event depth would be exceeded.");
    }
  }

  private sealed record ChildEvent(QueuedEvent Event, int InputOrder);

  private sealed class CanonicalOrderingKeyComparer : IComparer<IReadOnlyList<string>>
  {
    public static CanonicalOrderingKeyComparer Instance { get; } = new();

    public int Compare(IReadOnlyList<string>? x, IReadOnlyList<string>? y)
    {
      x ??= [];
      y ??= [];

      var sharedLength = Math.Min(x.Count, y.Count);
      for (var i = 0; i < sharedLength; i++)
      {
        var comparison = StringComparer.Ordinal.Compare(x[i], y[i]);
        if (comparison != 0)
        {
          return comparison;
        }
      }

      return x.Count.CompareTo(y.Count);
    }
  }
}

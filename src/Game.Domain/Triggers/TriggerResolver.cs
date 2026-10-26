using Game.Shared.Dsl;
using Game.Shared.Events;
using Game.Shared.Primitives;

namespace Game.Domain.Triggers;

public sealed class TriggerResolver : ITriggerResolver
{
  public TriggerResolutionResult Resolve(TriggerResolutionRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var diagnostics = ValidateTriggers(request.Triggers);
    if (diagnostics.Count > 0)
    {
      return TriggerResolutionResult.Rejected(diagnostics);
    }

    var matches = new List<TriggerMatch>();
    foreach (var committedEvent in request.Events
      .Where(@event => @event.Category == EventCategory.Committed)
      .OrderBy(@event => @event.EventSeq))
    {
      if (!TryMapEventType(committedEvent.EventType, out var triggerEventType))
      {
        continue;
      }

      foreach (var trigger in request.Triggers)
      {
        if (trigger.Trigger.EventType != triggerEventType ||
            !ControllerScopeMatches(trigger, committedEvent))
        {
          continue;
        }

        matches.Add(CreateMatch(committedEvent, trigger, triggerEventType));
      }
    }

    return TriggerResolutionResult.Accepted(matches
      .OrderBy(match => match.SourceEventSeq)
      .ThenBy(match => match.CanonicalOrderingKeys, CanonicalOrderingKeyComparer.Instance)
      .ThenBy(match => match.ControllerPlayerId, StringComparer.Ordinal)
      .ThenBy(match => match.SourceRef, StringComparer.Ordinal)
      .ThenBy(match => match.EffectId, StringComparer.Ordinal)
      .ThenBy(match => match.TriggerId, StringComparer.Ordinal)
      .ToArray());
  }

  private static List<TriggerResolutionDiagnostic> ValidateTriggers(IReadOnlyList<TriggerRegistration> triggers)
  {
    var diagnostics = new List<TriggerResolutionDiagnostic>();
    foreach (var trigger in triggers)
    {
      if (trigger.Trigger.EventFilter is not null)
      {
        diagnostics.Add(new TriggerResolutionDiagnostic(
          TriggerResolutionDiagnosticCode.UnsupportedEventFilter,
          "Trigger resolver event filtering is not implemented.",
          trigger.TriggerId));
      }

      if (!IsSupportedControllerScope(trigger.Trigger.ControllerScope))
      {
        diagnostics.Add(new TriggerResolutionDiagnostic(
          TriggerResolutionDiagnosticCode.UnsupportedControllerScope,
          $"Controller scope '{trigger.Trigger.ControllerScope}' is not supported by bootstrap trigger resolution.",
          trigger.TriggerId));
      }
    }

    return diagnostics;
  }

  private static TriggerMatch CreateMatch(
    CommittedEventRecord committedEvent,
    TriggerRegistration registration,
    EventType triggerEventType) =>
    new(
      $"trigger-match:{committedEvent.EventSeq:0000000000}:{registration.TriggerId}",
      committedEvent.EventSeq,
      committedEvent.EventType,
      triggerEventType,
      registration.TriggerId,
      registration.EffectId,
      registration.ControllerPlayerId,
      registration.SourceRef,
      registration.CanonicalOrderingKeys);

  private static bool ControllerScopeMatches(
    TriggerRegistration registration,
    CommittedEventRecord committedEvent)
  {
    var eventPlayerId = ResolvePlayerId(committedEvent);
    return registration.Trigger.ControllerScope.ToUpperInvariant() switch
    {
      "ANY" => true,
      "SELF" or "SOURCE_CONTROLLER" => StringComparer.Ordinal.Equals(
        eventPlayerId,
        registration.ControllerPlayerId),
      "OPPONENT" => !string.IsNullOrWhiteSpace(eventPlayerId) &&
        !StringComparer.Ordinal.Equals(eventPlayerId, registration.ControllerPlayerId),
      _ => false
    };
  }

  private static bool IsSupportedControllerScope(string controllerScope)
  {
    return controllerScope.ToUpperInvariant() is "ANY" or "SELF" or "SOURCE_CONTROLLER" or "OPPONENT";
  }

  private static string? ResolvePlayerId(CommittedEventRecord committedEvent)
  {
    var payloadPlayerId = committedEvent.Payload
      .Where(entry => StringComparer.Ordinal.Equals(entry.Key, "playerId"))
      .Select(entry => entry.Value.StringValue)
      .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    if (!string.IsNullOrWhiteSpace(payloadPlayerId))
    {
      return payloadPlayerId;
    }

    return committedEvent.SourceRefs
      .Concat(committedEvent.TargetRefs)
      .Where(eventRef => !string.IsNullOrWhiteSpace(eventRef.ControllerPlayerId))
      .OrderBy(EventRefOrderingKey, StringComparer.Ordinal)
      .Select(eventRef => eventRef.ControllerPlayerId)
      .FirstOrDefault();
  }

  private static bool TryMapEventType(
    CommittedEventType committedEventType,
    out EventType triggerEventType)
  {
    switch (committedEventType)
    {
      case CommittedEventType.CardPlayed:
        triggerEventType = EventType.CardPlayed;
        return true;
      case CommittedEventType.MinionSummoned:
        triggerEventType = EventType.CardSummoned;
        return true;
      case CommittedEventType.TurnStarted:
        triggerEventType = EventType.TurnStarted;
        return true;
      case CommittedEventType.TurnEnded:
        triggerEventType = EventType.TurnEnded;
        return true;
      case CommittedEventType.EntityDamaged:
        triggerEventType = EventType.DamageDealt;
        return true;
      case CommittedEventType.EntityDied:
        triggerEventType = EventType.EntityDied;
        return true;
      case CommittedEventType.CardDrawn:
        triggerEventType = EventType.CardDrawn;
        return true;
      default:
        triggerEventType = default;
        return false;
    }
  }

  private static string EventRefOrderingKey(EventRef eventRef) =>
    $"{eventRef.ControllerPlayerId ?? string.Empty}:{eventRef.RefType}:{eventRef.RefId}:{eventRef.Zone ?? string.Empty}";

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

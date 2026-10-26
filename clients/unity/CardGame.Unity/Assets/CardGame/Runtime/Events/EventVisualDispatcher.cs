using System;
using System.Collections.Generic;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.Events
{
  public sealed class EventVisualDispatcher
  {
    private static readonly Dictionary<string, EventVisualDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase)
    {
      ["GAME_STARTED"] = new("Match started", "setup", true, false, true),
      ["TURN_STARTED"] = new("Turn started", "turn", true, false, false),
      ["EXTRA_TURN_SCHEDULED"] = new("Extra turn scheduled", "turn", true, false, true),
      ["TURN_ENDED"] = new("Turn ended", "turn", true, false, false),
      ["CARD_PLAYED"] = new("Card played", "card", true, false, true),
      ["CARD_MOVED_ZONE"] = new("Card moved", "zone", true, false, false),
      ["CARD_DRAWN"] = new("Card drawn", "zone", true, false, false),
      ["CARD_DISCARDED"] = new("Card discarded", "zone", true, false, true),
      ["CARD_RETURNED_TO_HAND"] = new("Card returned to hand", "zone", true, false, true),
      ["MINION_SUMMONED"] = new("Minion summoned", "board", true, false, true),
      ["ENTITY_DAMAGED"] = new("Entity damaged", "combat", true, false, true),
      ["DAMAGE_WOULD_BE_DEALT"] = new("Damage incoming", "combat", false, false, true),
      ["REPLACEMENT_RESOLVED"] = new("Replacement resolved", "combat", true, false, true),
      ["ENTITY_HEALED"] = new("Entity healed", "combat", true, false, false),
      ["ENTITY_DIED"] = new("Entity died", "board", true, false, true),
      ["ENTITY_DESTROYED"] = new("Entity destroyed", "board", true, false, true),
      ["DEATHRATTLE_TRIGGERED"] = new("Deathrattle triggered", "trigger", true, false, true),
      ["SECRET_REVEALED"] = new("Secret revealed", "trigger", true, false, true),
      ["QUEST_PROGRESS_CHANGED"] = new("Quest progressed", "quest", true, false, true),
      ["MANA_CHANGED"] = new("Mana changed", "resource", true, false, false),
      ["MODIFIER_APPLIED"] = new("Modifier applied", "modifier", true, false, true),
      ["MODIFIER_EXPIRED"] = new("Modifier expired", "modifier", true, false, true),
      ["CHOICE_CREATED"] = new("Choice created", "choice", true, false, true),
      ["CHOICE_RESOLVED"] = new("Choice resolved", "choice", true, false, true),
      ["AURA_REFRESHED"] = new("Aura refreshed", "modifier", true, false, false),
      ["WEAPON_EQUIPPED"] = new("Weapon equipped", "equipment", true, false, false),
      ["LOCATION_PLAYED"] = new("Location played", "location", true, false, true),
      ["LOCATION_ACTIVATED"] = new("Location activated", "location", true, false, true),
      ["HERO_POWER_USED"] = new("Hero power used", "hero power", true, false, true),
      ["HERO_CARD_PLAYED"] = new("Hero card played", "hero", true, false, true),
      ["HERO_POWER_REPLACED"] = new("Hero power replaced", "hero power", true, false, true),
      ["RNG_ROLLED"] = new("Random outcome resolved", "random", false, false, true),
      ["GAME_ENDED"] = new("Game ended", "terminal", true, true, true),
      ["ENGINE_FAULTED"] = new("Engine faulted", "terminal", true, true, true)
    };

    public EventVisualMessage Dispatch(PublicMatchEventRecord evt)
    {
      if (evt == null)
      {
        return new EventVisualMessage("UNKNOWN", "Unknown event", "unknown", false, false, true, string.Empty);
      }

      if (!Definitions.TryGetValue(evt.EventType ?? string.Empty, out var definition))
      {
        return new EventVisualMessage(
          evt.EventType ?? "UNKNOWN",
          $"Event {evt.EventType ?? "UNKNOWN"}",
          "unknown",
          true,
          false,
          true,
          DescribeRefs(evt));
      }

      return new EventVisualMessage(
        evt.EventType,
        definition.Message,
        definition.Category,
        definition.RefreshState,
        definition.Blocking,
        definition.RequestUiRefresh,
        DescribeRefs(evt));
    }

    public static IReadOnlyCollection<string> KnownEventTypes => Definitions.Keys;

    private static string DescribeRefs(PublicMatchEventRecord evt)
    {
      var refs = new List<string>();
      foreach (var reference in evt.SourceRefs ?? new List<PublicEventRef>())
      {
        refs.Add(reference.RefType + ":" + reference.RefId);
      }

      foreach (var reference in evt.TargetRefs ?? new List<PublicEventRef>())
      {
        refs.Add(reference.RefType + ":" + reference.RefId);
      }

      return refs.Count == 0 ? string.Empty : string.Join(" ", refs.GetRange(0, Math.Min(3, refs.Count)));
    }

    private sealed class EventVisualDefinition
    {
      public string Message { get; }
      public string Category { get; }
      public bool RefreshState { get; }
      public bool Blocking { get; }
      public bool RequestUiRefresh { get; }

      public EventVisualDefinition(string message, string category, bool refreshState, bool blocking, bool requestUiRefresh)
      {
        Message = message;
        Category = category;
        RefreshState = refreshState;
        Blocking = blocking;
        RequestUiRefresh = requestUiRefresh;
      }
    }
  }

  public sealed class EventVisualMessage
  {
    public string EventType { get; }
    public string Message { get; }
    public string Category { get; }
    public bool RefreshState { get; }
    public bool Blocking { get; }
    public bool RequestUiRefresh { get; }
    public string Summary { get; }

    public EventVisualMessage(
      string eventType,
      string message,
      string category,
      bool refreshState,
      bool blocking,
      bool requestUiRefresh,
      string summary)
    {
      EventType = eventType;
      Message = message;
      Category = category ?? "unknown";
      RefreshState = refreshState;
      Blocking = blocking;
      RequestUiRefresh = requestUiRefresh;
      Summary = summary;
    }

    public string ToLogLine(long eventSeq) =>
      $"{eventSeq} {EventType}: {Message}" + (string.IsNullOrWhiteSpace(Summary) ? string.Empty : " " + Summary);
  }
}

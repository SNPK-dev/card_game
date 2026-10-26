using System;
using System.Collections.Generic;
using System.Linq;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.Presentation
{
  public enum PresentationPlaybackStatus
  {
    Idle,
    Playing,
    Blocked
  }

  public enum PresentationIntentType
  {
    Generic,
    Setup,
    TurnStatus,
    CardZone,
    BoardCombat,
    ChoiceStatus,
    ModifierStatus,
    EquipmentStatus,
    HeroStatus,
    ConnectionStatus,
    ReplayStatus,
    Terminal,
    Fault
  }

  public sealed class PresentationIntent
  {
    public PresentationIntentType IntentType { get; set; } = PresentationIntentType.Generic;
    public string EventType { get; set; } = string.Empty;
    public long EventSeq { get; set; }
    public float DurationSeconds { get; set; } = 0.35f;
    public string ChoreographyLabel { get; set; } = string.Empty;
    public bool RequestsStateRefresh { get; set; }
    public bool BlocksQueue { get; set; }
    public string Diagnostic { get; set; } = string.Empty;
  }

  public sealed class PresentationTimelineStep
  {
    public long EventSeq { get; set; }
    public PresentationIntentType IntentType { get; set; }
    public float DurationSeconds { get; set; }
    public string ChoreographyLabel { get; set; } = string.Empty;
    public bool Blocking { get; set; }
  }

  public sealed class PresentationTimeline
  {
    public List<PresentationTimelineStep> Steps { get; set; } = new();
  }

  public sealed class PresentationPlaybackClock
  {
    public float NowSeconds { get; private set; }

    public void Advance(float seconds)
    {
      NowSeconds += Math.Max(0f, seconds);
    }
  }

  public sealed class PresentationChoreographyLabeler
  {
    public string Describe(PresentationIntent intent) =>
      intent?.IntentType switch
      {
        PresentationIntentType.CardZone => "card movement",
        PresentationIntentType.BoardCombat => "combat/damage",
        PresentationIntentType.TurnStatus => "turn changed",
        PresentationIntentType.ChoiceStatus => "choice opened/resolved",
        PresentationIntentType.Terminal => "final blocking banner",
        PresentationIntentType.Fault => "blocking fault banner",
        PresentationIntentType.ConnectionStatus => "connection/reconnect status",
        PresentationIntentType.ReplayStatus => "replay verification status",
        _ => "generic event"
      };
  }

  public sealed class PresentationPlaybackReport
  {
    public int Enqueued { get; set; }
    public int Played { get; set; }
    public int DuplicatesIgnored { get; set; }
    public int AnimationsPlayed { get; set; }
    public int SkippedAnimations { get; set; }
    public bool Blocked { get; set; }
    public List<string> Lines { get; set; } = new();
  }

  public sealed class PresentationIntentQueue
  {
    private readonly Queue<PresentationIntent> intents = new();
    private readonly HashSet<long> seenEventSeqs = new();
    public PresentationPlaybackStatus Status { get; private set; } = PresentationPlaybackStatus.Idle;
    public int Count => intents.Count;

    public bool Enqueue(PresentationIntent intent)
    {
      if (intent == null)
      {
        return false;
      }

      if (intent.EventSeq > 0 && !seenEventSeqs.Add(intent.EventSeq))
      {
        return false;
      }

      if (intent.BlocksQueue)
      {
        intents.Clear();
        Status = PresentationPlaybackStatus.Blocked;
      }
      else if (Status != PresentationPlaybackStatus.Blocked)
      {
        Status = PresentationPlaybackStatus.Playing;
      }

      intents.Enqueue(intent);
      return true;
    }

    public int EnqueueReconnectMissedEvents(IEnumerable<PublicMatchEventRecord> missedEvents, PresentationEventRouter router)
    {
      router ??= new PresentationEventRouter();
      var accepted = 0;
      foreach (var evt in (missedEvents ?? Array.Empty<PublicMatchEventRecord>()).OrderBy(evt => evt.EventSeq))
      {
        if (Enqueue(router.Route(evt)))
        {
          accepted++;
        }
      }

      return accepted;
    }

    public bool TryDequeue(out PresentationIntent intent)
    {
      if (intents.Count == 0)
      {
        intent = null;
        if (Status == PresentationPlaybackStatus.Playing)
        {
          Status = PresentationPlaybackStatus.Idle;
        }

        return false;
      }

      intent = intents.Dequeue();
      if (intents.Count == 0 && Status == PresentationPlaybackStatus.Playing)
      {
        Status = PresentationPlaybackStatus.Idle;
      }

      return true;
    }
  }

  public sealed class PresentationEventRouter
  {
    public static readonly IReadOnlyCollection<string> KnownPublicEventTypes = new[]
    {
      "GAME_STARTED",
      "TURN_STARTED",
      "EXTRA_TURN_SCHEDULED",
      "TURN_ENDED",
      "CARD_PLAYED",
      "CARD_MOVED_ZONE",
      "CARD_DRAWN",
      "CARD_DISCARDED",
      "CARD_RETURNED_TO_HAND",
      "MINION_SUMMONED",
      "ENTITY_DAMAGED",
      "DAMAGE_WOULD_BE_DEALT",
      "REPLACEMENT_RESOLVED",
      "ENTITY_HEALED",
      "MANA_CHANGED",
      "ENTITY_DIED",
      "ENTITY_DESTROYED",
      "DEATHRATTLE_TRIGGERED",
      "SECRET_REVEALED",
      "QUEST_PROGRESS_CHANGED",
      "MODIFIER_APPLIED",
      "MODIFIER_EXPIRED",
      "CHOICE_CREATED",
      "CHOICE_RESOLVED",
      "AURA_REFRESHED",
      "WEAPON_EQUIPPED",
      "LOCATION_PLAYED",
      "LOCATION_ACTIVATED",
      "HERO_CARD_PLAYED",
      "HERO_POWER_USED",
      "HERO_POWER_REPLACED",
      "RNG_ROLLED",
      "GAME_ENDED",
      "ENGINE_FAULTED"
    };

    public PresentationIntent Route(PublicMatchEventRecord evt)
    {
      var eventType = evt?.EventType ?? string.Empty;
      var terminal = eventType == "GAME_ENDED" || eventType == "ENGINE_FAULTED";
      var intentType = ToIntentType(eventType);
      return new PresentationIntent
      {
        EventType = eventType,
        EventSeq = evt?.EventSeq ?? 0,
        IntentType = intentType,
        DurationSeconds = DurationFor(intentType),
        ChoreographyLabel = new PresentationChoreographyLabeler().Describe(new PresentationIntent { IntentType = intentType }),
        RequestsStateRefresh = eventType != "RNG_ROLLED",
        BlocksQueue = terminal,
        Diagnostic = intentType == PresentationIntentType.Generic && !KnownPublicEventTypes.Contains(eventType)
          ? $"Unsupported event type '{eventType}' routed to generic presentation fallback."
          : string.Empty
      };
    }

    public PresentationIntent RouteStatus(string eventType, PresentationIntentType intentType, string diagnostic) =>
      new()
      {
        EventType = eventType ?? string.Empty,
        EventSeq = 0,
        IntentType = intentType,
        DurationSeconds = DurationFor(intentType),
        ChoreographyLabel = new PresentationChoreographyLabeler().Describe(new PresentationIntent { IntentType = intentType }),
        RequestsStateRefresh = false,
        BlocksQueue = intentType is PresentationIntentType.Terminal or PresentationIntentType.Fault,
        Diagnostic = diagnostic ?? string.Empty
      };

    private static PresentationIntentType ToIntentType(string eventType) =>
      eventType switch
      {
        "GAME_STARTED" => PresentationIntentType.Setup,
        "TURN_STARTED" or "TURN_ENDED" or "EXTRA_TURN_SCHEDULED" => PresentationIntentType.TurnStatus,
        "CARD_PLAYED" or "CARD_DRAWN" or "CARD_MOVED_ZONE" or "CARD_RETURNED_TO_HAND" or "CARD_DISCARDED" => PresentationIntentType.CardZone,
        "MINION_SUMMONED" or "ENTITY_DAMAGED" or "ENTITY_HEALED" or "ENTITY_DIED" or "ENTITY_DESTROYED" or "DEATHRATTLE_TRIGGERED" or "DAMAGE_WOULD_BE_DEALT" or "REPLACEMENT_RESOLVED" => PresentationIntentType.BoardCombat,
        "CHOICE_CREATED" or "CHOICE_RESOLVED" => PresentationIntentType.ChoiceStatus,
        "AURA_REFRESHED" or "MODIFIER_APPLIED" or "MODIFIER_EXPIRED" or "SECRET_REVEALED" or "QUEST_PROGRESS_CHANGED" or "MANA_CHANGED" or "RNG_ROLLED" => PresentationIntentType.ModifierStatus,
        "WEAPON_EQUIPPED" or "LOCATION_PLAYED" or "LOCATION_ACTIVATED" => PresentationIntentType.EquipmentStatus,
        "HERO_CARD_PLAYED" or "HERO_POWER_USED" or "HERO_POWER_REPLACED" => PresentationIntentType.HeroStatus,
        "RECONNECT_STARTED" or "RECONNECT_COMPLETED" => PresentationIntentType.ConnectionStatus,
        "REPLAY_VERIFIED" or "REPLAY_MISMATCH" => PresentationIntentType.ReplayStatus,
        "GAME_ENDED" => PresentationIntentType.Terminal,
        "ENGINE_FAULTED" => PresentationIntentType.Fault,
        _ => PresentationIntentType.Generic
      };

    private static float DurationFor(PresentationIntentType intentType) =>
      intentType switch
      {
        PresentationIntentType.Setup => 0.1f,
        PresentationIntentType.TurnStatus => 0.25f,
        PresentationIntentType.CardZone => 0.4f,
        PresentationIntentType.BoardCombat => 0.35f,
        PresentationIntentType.ChoiceStatus => 0.3f,
        PresentationIntentType.Terminal => 0.5f,
        PresentationIntentType.Fault => 999f,
        PresentationIntentType.ConnectionStatus => 0.15f,
        PresentationIntentType.ReplayStatus => 0.2f,
        _ => 0.35f
      };
  }

  public sealed class PresentationPlaybackController
  {
    private readonly PresentationIntentQueue queue;

    public PresentationPlaybackController(PresentationIntentQueue queue)
    {
      this.queue = queue ?? new PresentationIntentQueue();
    }

    public PresentationPlaybackStatus Status => queue.Status;

    public bool TryAdvance(out PresentationIntent intent) => queue.TryDequeue(out intent);
  }

  public sealed class PresentationDiagnostics
  {
    public IReadOnlyList<string> Messages { get; set; } = Array.Empty<string>();

    public static PresentationDiagnostics FromIntent(PresentationIntent intent) =>
      new()
      {
        Messages = string.IsNullOrWhiteSpace(intent?.Diagnostic)
          ? Array.Empty<string>()
          : new[] { intent.Diagnostic }
      };
  }
}

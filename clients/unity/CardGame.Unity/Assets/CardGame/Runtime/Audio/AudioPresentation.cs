using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Unity.Client.Presentation;

namespace CardGame.Unity.Client.Audio
{
  public enum AudioCategory
  {
    UI,
    Card,
    Combat,
    Turn,
    Choice,
    Connection,
    Replay,
    Fault,
    Equipment,
    Modifier,
    Hero,
    Game
  }

  public sealed class AudioCue
  {
    public string CueId { get; set; } = string.Empty;
    public AudioCategory Category { get; set; }
    public string AssetRef { get; set; } = string.Empty;
    public float DefaultVolume { get; set; } = 1f;
  }

  public sealed class AudioCueRegistry
  {
    private readonly Dictionary<string, AudioCue> cues = new(StringComparer.OrdinalIgnoreCase);

    public AudioCueRegistry(IEnumerable<AudioCue> initialCues = null)
    {
      foreach (var cue in initialCues ?? DefaultCues())
      {
        if (!string.IsNullOrWhiteSpace(cue?.CueId))
        {
          cues[cue.CueId] = cue;
        }
      }
    }

    public bool TryGet(string cueId, out AudioCue cue) => cues.TryGetValue(cueId ?? string.Empty, out cue);

    public static IReadOnlyList<AudioCue> DefaultCues() => new[]
    {
      Cue("ui.button.click", AudioCategory.UI),
      Cue("ui.command.submit", AudioCategory.UI),
      Cue("card.play", AudioCategory.Card),
      Cue("card.draw", AudioCategory.Card),
      Cue("combat.damage", AudioCategory.Combat),
      Cue("combat.heal", AudioCategory.Combat),
      Cue("combat.death", AudioCategory.Combat),
      Cue("trigger.deathrattle", AudioCategory.Modifier),
      Cue("trigger.secret", AudioCategory.Modifier),
      Cue("trigger.quest", AudioCategory.Modifier),
      Cue("modifier.buff", AudioCategory.Modifier),
      Cue("modifier.expire", AudioCategory.Modifier),
      Cue("modifier.aura", AudioCategory.Modifier),
      Cue("turn.start", AudioCategory.Turn),
      Cue("turn.end", AudioCategory.Turn),
      Cue("turn.extra", AudioCategory.Turn),
      Cue("choice.open", AudioCategory.Choice),
      Cue("choice.resolve", AudioCategory.Choice),
      Cue("connection.reconnect.success", AudioCategory.Connection),
      Cue("connection.reconnect.fail", AudioCategory.Connection),
      Cue("replay.verified", AudioCategory.Replay),
      Cue("replay.mismatch", AudioCategory.Replay),
      Cue("fault.blocking", AudioCategory.Fault),
      Cue("equipment.activate", AudioCategory.Equipment),
      Cue("modifier.trigger", AudioCategory.Modifier),
      Cue("hero.action", AudioCategory.Hero),
      Cue("game.start", AudioCategory.Game),
      Cue("event.generic", AudioCategory.UI)
    };

    private static AudioCue Cue(string id, AudioCategory category) =>
      new()
      {
        CueId = id,
        Category = category,
        AssetRef = "resource://Audio/Presentation/" + id.Replace('.', '-'),
        DefaultVolume = 1f
      };
  }

  public sealed class AudioVolumeSettings
  {
    private readonly Dictionary<AudioCategory, float> categoryVolumes = new();
    private float masterVolume = 1f;

    public bool Muted { get; set; }

    public float MasterVolume
    {
      get => masterVolume;
      set => masterVolume = Clamp(value);
    }

    public void SetCategoryVolume(AudioCategory category, float value) => categoryVolumes[category] = Clamp(value);

    public float CategoryVolume(AudioCategory category) =>
      categoryVolumes.TryGetValue(category, out var value) ? value : 1f;

    public float EffectiveVolume(AudioCue cue) =>
      Muted || cue == null ? 0f : Clamp(MasterVolume * CategoryVolume(cue.Category) * cue.DefaultVolume);

    private static float Clamp(float value) => Math.Max(0f, Math.Min(1f, value));
  }

  public sealed class AudioSettingsViewModel
  {
    public bool Muted { get; set; }
    public float MasterVolume { get; set; }
    public IReadOnlyDictionary<AudioCategory, float> CategoryVolumes { get; set; } = new Dictionary<AudioCategory, float>();

    public static AudioSettingsViewModel From(AudioVolumeSettings settings) =>
      new()
      {
        Muted = settings?.Muted ?? false,
        MasterVolume = settings?.MasterVolume ?? 1f,
        CategoryVolumes = Enum.GetValues(typeof(AudioCategory))
          .Cast<AudioCategory>()
          .ToDictionary(category => category, category => settings?.CategoryVolume(category) ?? 1f)
      };
  }

  public sealed class AudioPlaybackRequest
  {
    public string CueId { get; set; } = string.Empty;
    public AudioCategory Category { get; set; }
    public string AssetRef { get; set; } = string.Empty;
    public float Volume { get; set; }
    public bool Suppressed { get; set; }
    public string Reason { get; set; } = string.Empty;
  }

  public sealed class AudioPlaybackController
  {
    private readonly AudioCueRegistry registry;
    private readonly AudioVolumeSettings settings;

    public AudioPlaybackController(AudioCueRegistry registry = null, AudioVolumeSettings settings = null)
    {
      this.registry = registry ?? new AudioCueRegistry();
      this.settings = settings ?? new AudioVolumeSettings();
    }

    public List<AudioPlaybackRequest> Requests { get; } = new();

    public AudioPlaybackRequest Request(string cueId)
    {
      if (!registry.TryGet(cueId, out var cue))
      {
        var missing = new AudioPlaybackRequest { CueId = cueId ?? string.Empty, Suppressed = true, Reason = "missing-cue" };
        Requests.Add(missing);
        return missing;
      }

      var volume = settings.EffectiveVolume(cue);
      var request = new AudioPlaybackRequest
      {
        CueId = cue.CueId,
        Category = cue.Category,
        AssetRef = cue.AssetRef,
        Volume = volume,
        Suppressed = volume <= 0f,
        Reason = volume <= 0f ? "muted-or-zero-volume" : string.Empty
      };
      Requests.Add(request);
      return request;
    }
  }

  public sealed class AudioEventRouter
  {
    public string CueForEvent(string eventType) =>
      (eventType ?? string.Empty).ToUpperInvariant() switch
      {
        "BUTTON_CLICKED" => "ui.button.click",
        "COMMAND_SUBMITTED" => "ui.command.submit",
        "CARD_PLAYED" or "MINION_SUMMONED" => "card.play",
        "CARD_DRAWN" or "CARD_MOVED_ZONE" or "CARD_RETURNED_TO_HAND" or "CARD_DISCARDED" => "card.draw",
        "ENTITY_DAMAGED" => "combat.damage",
        "ENTITY_DIED" or "ENTITY_DESTROYED" => "combat.death",
        "ENTITY_HEALED" => "combat.heal",
        "TURN_STARTED" => "turn.start",
        "EXTRA_TURN_SCHEDULED" => "turn.extra",
        "TURN_ENDED" => "turn.end",
        "CHOICE_CREATED" => "choice.open",
        "CHOICE_RESOLVED" => "choice.resolve",
        "DEATHRATTLE_TRIGGERED" => "trigger.deathrattle",
        "SECRET_REVEALED" => "trigger.secret",
        "QUEST_PROGRESS_CHANGED" => "trigger.quest",
        "MODIFIER_APPLIED" => "modifier.buff",
        "MODIFIER_EXPIRED" => "modifier.expire",
        "AURA_REFRESHED" => "modifier.aura",
        "DAMAGE_WOULD_BE_DEALT" or "REPLACEMENT_RESOLVED" or "MANA_CHANGED" or "RNG_ROLLED" => "modifier.trigger",
        "WEAPON_EQUIPPED" or "LOCATION_PLAYED" or "LOCATION_ACTIVATED" => "equipment.activate",
        "HERO_CARD_PLAYED" or "HERO_POWER_USED" or "HERO_POWER_REPLACED" => "hero.action",
        "GAME_STARTED" => "game.start",
        "RECONNECT_COMPLETED" => "connection.reconnect.success",
        "RECONNECT_FAILED" => "connection.reconnect.fail",
        "REPLAY_VERIFIED" => "replay.verified",
        "REPLAY_MISMATCH" => "replay.mismatch",
        "ENGINE_FAULTED" or "GAME_ENDED" => "fault.blocking",
        _ => "event.generic"
      };

    public string CueForIntent(PresentationIntent intent) => CueForEvent(intent?.EventType);
  }
}

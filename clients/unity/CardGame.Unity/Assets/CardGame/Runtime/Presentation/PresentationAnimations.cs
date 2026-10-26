using System;
using System.Collections.Generic;

namespace CardGame.Unity.Client.Presentation
{
  public sealed class PresentationAnimationBinding
  {
    public string EventType { get; set; } = string.Empty;
    public PresentationIntentType IntentType { get; set; }
    public string AnimatorName { get; set; } = string.Empty;
    public float DurationSeconds { get; set; }
    public bool Blocking { get; set; }
  }

  public interface PresentationIntentAnimator
  {
    string Name { get; }
    string Animate(PresentationIntent intent);
  }

  public abstract class PresentationIntentAnimatorBase : PresentationIntentAnimator
  {
    protected PresentationIntentAnimatorBase(string name)
    {
      Name = name;
    }

    public string Name { get; }

    public virtual string Animate(PresentationIntent intent) =>
      $"{Name}:{intent?.EventSeq ?? 0}:{intent?.EventType ?? string.Empty}";
  }

  public sealed class CardMoveAnimator : PresentationIntentAnimatorBase { public CardMoveAnimator() : base(nameof(CardMoveAnimator)) { } }
  public sealed class CardPlayAnimator : PresentationIntentAnimatorBase { public CardPlayAnimator() : base(nameof(CardPlayAnimator)) { } }
  public sealed class CardDrawAnimator : PresentationIntentAnimatorBase { public CardDrawAnimator() : base(nameof(CardDrawAnimator)) { } }
  public sealed class DamageAnimator : PresentationIntentAnimatorBase { public DamageAnimator() : base(nameof(DamageAnimator)) { } }
  public sealed class HealAnimator : PresentationIntentAnimatorBase { public HealAnimator() : base(nameof(HealAnimator)) { } }
  public sealed class DeathAnimator : PresentationIntentAnimatorBase { public DeathAnimator() : base(nameof(DeathAnimator)) { } }
  public sealed class TurnBannerAnimator : PresentationIntentAnimatorBase { public TurnBannerAnimator() : base(nameof(TurnBannerAnimator)) { } }
  public sealed class ChoiceAnimator : PresentationIntentAnimatorBase { public ChoiceAnimator() : base(nameof(ChoiceAnimator)) { } }
  public sealed class ReconnectAnimator : PresentationIntentAnimatorBase { public ReconnectAnimator() : base(nameof(ReconnectAnimator)) { } }
  public sealed class ReplayAnimator : PresentationIntentAnimatorBase { public ReplayAnimator() : base(nameof(ReplayAnimator)) { } }
  public sealed class FaultAnimator : PresentationIntentAnimatorBase { public FaultAnimator() : base(nameof(FaultAnimator)) { } }

  public sealed class PresentationAnimationRegistry
  {
    private readonly Dictionary<string, PresentationAnimationBinding> bindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PresentationIntentAnimator> animators = new(StringComparer.OrdinalIgnoreCase);

    public PresentationAnimationRegistry()
    {
      Register(new CardMoveAnimator());
      Register(new CardPlayAnimator());
      Register(new CardDrawAnimator());
      Register(new DamageAnimator());
      Register(new HealAnimator());
      Register(new DeathAnimator());
      Register(new TurnBannerAnimator());
      Register(new ChoiceAnimator());
      Register(new ReconnectAnimator());
      Register(new ReplayAnimator());
      Register(new FaultAnimator());

      Bind("GAME_STARTED", PresentationIntentType.Setup, nameof(TurnBannerAnimator), 0.1f, true);
      Bind("TURN_STARTED", PresentationIntentType.TurnStatus, nameof(TurnBannerAnimator), 0.25f);
      Bind("EXTRA_TURN_SCHEDULED", PresentationIntentType.TurnStatus, nameof(TurnBannerAnimator), 0.25f);
      Bind("TURN_ENDED", PresentationIntentType.TurnStatus, nameof(TurnBannerAnimator), 0.25f);
      Bind("CARD_PLAYED", PresentationIntentType.CardZone, nameof(CardPlayAnimator), 0.4f, true);
      Bind("CARD_DRAWN", PresentationIntentType.CardZone, nameof(CardDrawAnimator), 0.3f);
      Bind("CARD_MOVED_ZONE", PresentationIntentType.CardZone, nameof(CardMoveAnimator), 0.3f);
      Bind("CARD_DISCARDED", PresentationIntentType.CardZone, nameof(CardMoveAnimator), 0.3f);
      Bind("CARD_RETURNED_TO_HAND", PresentationIntentType.CardZone, nameof(CardMoveAnimator), 0.3f);
      Bind("MINION_SUMMONED", PresentationIntentType.BoardCombat, nameof(CardPlayAnimator), 0.35f, true);
      Bind("ENTITY_DAMAGED", PresentationIntentType.BoardCombat, nameof(DamageAnimator), 0.3f, true);
      Bind("DAMAGE_WOULD_BE_DEALT", PresentationIntentType.BoardCombat, nameof(DamageAnimator), 0.2f);
      Bind("REPLACEMENT_RESOLVED", PresentationIntentType.BoardCombat, nameof(DamageAnimator), 0.25f);
      Bind("ENTITY_HEALED", PresentationIntentType.BoardCombat, nameof(HealAnimator), 0.3f);
      Bind("ENTITY_DIED", PresentationIntentType.BoardCombat, nameof(DeathAnimator), 0.35f, true);
      Bind("ENTITY_DESTROYED", PresentationIntentType.BoardCombat, nameof(DeathAnimator), 0.35f, true);
      Bind("DEATHRATTLE_TRIGGERED", PresentationIntentType.BoardCombat, nameof(DeathAnimator), 0.3f, true);
      Bind("SECRET_REVEALED", PresentationIntentType.ModifierStatus, nameof(ChoiceAnimator), 0.3f, true);
      Bind("QUEST_PROGRESS_CHANGED", PresentationIntentType.ModifierStatus, nameof(ChoiceAnimator), 0.25f);
      Bind("MANA_CHANGED", PresentationIntentType.ModifierStatus, nameof(TurnBannerAnimator), 0.15f);
      Bind("MODIFIER_APPLIED", PresentationIntentType.ModifierStatus, nameof(ChoiceAnimator), 0.2f);
      Bind("MODIFIER_EXPIRED", PresentationIntentType.ModifierStatus, nameof(ChoiceAnimator), 0.2f);
      Bind("AURA_REFRESHED", PresentationIntentType.ModifierStatus, nameof(ChoiceAnimator), 0.15f);
      Bind("CHOICE_CREATED", PresentationIntentType.ChoiceStatus, nameof(ChoiceAnimator), 0.2f, true);
      Bind("CHOICE_RESOLVED", PresentationIntentType.ChoiceStatus, nameof(ChoiceAnimator), 0.25f);
      Bind("WEAPON_EQUIPPED", PresentationIntentType.EquipmentStatus, nameof(CardPlayAnimator), 0.35f);
      Bind("LOCATION_PLAYED", PresentationIntentType.EquipmentStatus, nameof(CardPlayAnimator), 0.35f);
      Bind("LOCATION_ACTIVATED", PresentationIntentType.EquipmentStatus, nameof(CardPlayAnimator), 0.35f, true);
      Bind("HERO_POWER_USED", PresentationIntentType.HeroStatus, nameof(CardPlayAnimator), 0.35f, true);
      Bind("HERO_CARD_PLAYED", PresentationIntentType.HeroStatus, nameof(CardPlayAnimator), 0.4f, true);
      Bind("HERO_POWER_REPLACED", PresentationIntentType.HeroStatus, nameof(CardPlayAnimator), 0.3f);
      Bind("RNG_ROLLED", PresentationIntentType.ModifierStatus, nameof(ChoiceAnimator), 0.2f);
      Bind("GAME_ENDED", PresentationIntentType.Terminal, nameof(TurnBannerAnimator), 0.5f, true);
      Bind("ENGINE_FAULTED", PresentationIntentType.Fault, nameof(FaultAnimator), 999f, true);
      Bind("RECONNECT_COMPLETED", PresentationIntentType.ConnectionStatus, nameof(ReconnectAnimator), 0.15f);
      Bind("REPLAY_VERIFIED", PresentationIntentType.ReplayStatus, nameof(ReplayAnimator), 0.2f);
      Bind("REPLAY_MISMATCH", PresentationIntentType.ReplayStatus, nameof(ReplayAnimator), 0.2f, true);
    }

    public IReadOnlyCollection<PresentationAnimationBinding> Bindings => bindings.Values;

    public PresentationAnimationBinding Resolve(PresentationIntent intent)
    {
      if (intent != null && bindings.TryGetValue(intent.EventType, out var binding))
      {
        return binding;
      }

      return new PresentationAnimationBinding
      {
        EventType = intent?.EventType ?? string.Empty,
        IntentType = intent?.IntentType ?? PresentationIntentType.Generic,
        AnimatorName = nameof(FaultAnimator),
        DurationSeconds = intent?.DurationSeconds > 0f ? intent.DurationSeconds : 0.35f,
        Blocking = intent?.BlocksQueue == true
      };
    }

    public string Animate(PresentationIntent intent)
    {
      var binding = Resolve(intent);
      return animators.TryGetValue(binding.AnimatorName, out var animator)
        ? animator.Animate(intent)
        : $"FallbackAnimator:{intent?.EventSeq ?? 0}:{intent?.EventType ?? string.Empty}";
    }

    private void Register(PresentationIntentAnimator animator) => animators[animator.Name] = animator;

    private void Bind(string eventType, PresentationIntentType intentType, string animatorName, float durationSeconds, bool blocking = false) =>
      bindings[eventType] = new PresentationAnimationBinding
      {
        EventType = eventType,
        IntentType = intentType,
        AnimatorName = animatorName,
        DurationSeconds = durationSeconds,
        Blocking = blocking
      };
  }
}

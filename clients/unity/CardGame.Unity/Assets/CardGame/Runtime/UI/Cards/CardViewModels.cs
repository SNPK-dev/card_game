using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Unity.Client.UI.Theme;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.UI.Cards
{
  public enum CardDisplayIdentity
  {
    None,
    Public,
    PrivateToViewer
  }

  public enum CardZoneDisplayPolicy
  {
    CountOnly,
    CardBack,
    VisibleCards
  }

  public sealed class CardVisibilityPolicy
  {
    public static readonly CardVisibilityPolicy PlayerSelf = new(CardZoneDisplayPolicy.VisibleCards);
    public static readonly CardVisibilityPolicy HiddenZone = new(CardZoneDisplayPolicy.CardBack);
    public static readonly CardVisibilityPolicy Deck = new(CardZoneDisplayPolicy.CountOnly);

    public CardZoneDisplayPolicy ZonePolicy { get; }

    public CardVisibilityPolicy(CardZoneDisplayPolicy zonePolicy)
    {
      ZonePolicy = zonePolicy;
    }

    public bool AllowsIdentity(PublicCardInstanceView card)
    {
      if (ZonePolicy != CardZoneDisplayPolicy.VisibleCards || card == null)
      {
        return false;
      }

      return string.Equals(card.Visibility, "VISIBLE", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(card.Visibility, "PUBLIC", StringComparison.OrdinalIgnoreCase);
    }
  }

  public sealed class CardBackViewModel
  {
    public int Count { get; set; }
    public string Zone { get; set; } = string.Empty;
  }

  public sealed class CardViewModel
  {
    public bool IsPlaceholder { get; set; }
    public bool IsFaceUp { get; set; }
    public int Count { get; set; } = 1;
    public string Zone { get; set; } = string.Empty;
    public string CardInstanceId { get; set; } = string.Empty;
    public string CardId { get; set; } = string.Empty;
    public string CardVersion { get; set; } = string.Empty;
    public string OwnerPlayerId { get; set; } = string.Empty;
    public string CardType { get; set; } = string.Empty;
    public string Visibility { get; set; } = string.Empty;
    public int Cost { get; set; }
    public int Attack { get; set; }
    public int Health { get; set; }
    public string RulesText { get; set; } = string.Empty;
    public IReadOnlyList<string> Keywords { get; set; } = Array.Empty<string>();
    public CardDisplayIdentity DisplayIdentity { get; set; }
    public string DisplayLabel { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool Playable { get; set; }
    public string DisabledReason { get; set; } = string.Empty;
    public bool Disabled => !string.IsNullOrWhiteSpace(DisabledReason);
    public ThemedSurfaceStyle Style { get; set; } = GameThemeStyles.CardBack(GameTheme.CreateDefault());
  }

  public sealed class CardViewModelFactory
  {
    public CardViewModel FromCard(PublicCardInstanceView card, CardVisibilityPolicy policy, string zone)
    {
      policy ??= CardVisibilityPolicy.HiddenZone;
      if (!policy.AllowsIdentity(card))
      {
        return Placeholder(zone, 1, policy.ZonePolicy);
      }

      return new CardViewModel
      {
        IsPlaceholder = false,
        IsFaceUp = true,
        Zone = zone ?? string.Empty,
        CardInstanceId = card.CardInstanceId ?? string.Empty,
        CardId = card.CardId ?? string.Empty,
        CardVersion = card.CardVersion ?? string.Empty,
        OwnerPlayerId = card.OwnerPlayerId ?? string.Empty,
        CardType = card.CardType ?? string.Empty,
        Visibility = card.Visibility ?? string.Empty,
        DisplayIdentity = CardDisplayIdentity.PrivateToViewer,
        DisplayLabel = string.IsNullOrWhiteSpace(card.CardId) ? "Visible card" : card.CardId,
        Title = string.IsNullOrWhiteSpace(card.CardId) ? "Visible card" : card.CardId,
        Cost = 0,
        Attack = 0,
        Health = 0,
        RulesText = card.CardType ?? string.Empty,
        Keywords = Array.Empty<string>(),
        Style = GameThemeStyles.CardFace(GameTheme.CreateDefault(), playable: false, disabled: false)
      };
    }

    public CardViewModel FromZone(PublicZoneView zone, CardVisibilityPolicy policy)
    {
      policy ??= CardVisibilityPolicy.HiddenZone;
      var count = Math.Max(zone?.Count ?? 0, zone?.Cards?.Count ?? 0);
      if (policy.ZonePolicy != CardZoneDisplayPolicy.VisibleCards)
      {
        return Placeholder(zone?.Zone, count, policy.ZonePolicy);
      }

      var firstVisible = zone?.Cards?.Find(card => policy.AllowsIdentity(card));
      return firstVisible == null
        ? Placeholder(zone?.Zone, count, policy.ZonePolicy)
        : FromCard(firstVisible, policy, zone?.Zone ?? string.Empty);
    }

    public CardBackViewModel ToCardBack(PublicZoneView zone) =>
      new()
      {
        Count = Math.Max(zone?.Count ?? 0, zone?.Cards?.Count ?? 0),
        Zone = zone?.Zone ?? string.Empty
      };

    private static CardViewModel Placeholder(string zone, int count, CardZoneDisplayPolicy policy) =>
      new()
      {
        IsPlaceholder = true,
        IsFaceUp = false,
        Count = Math.Max(0, count),
        Zone = zone ?? string.Empty,
        DisplayIdentity = CardDisplayIdentity.None,
        DisplayLabel = policy == CardZoneDisplayPolicy.CountOnly ? $"{Math.Max(0, count)} cards" : "Card back",
        Title = policy == CardZoneDisplayPolicy.CountOnly ? "Deck" : "Hidden card",
        Style = GameThemeStyles.CardBack(GameTheme.CreateDefault())
      };
  }

  public sealed class CardFaceView
  {
    public CardViewModel LastBound { get; private set; }
    public void Bind(CardViewModel model) => LastBound = model;
  }

  public sealed class CardBackView
  {
    public CardBackViewModel LastBound { get; private set; }
    public void Bind(CardBackViewModel model) => LastBound = model;
  }

  public sealed class CardFrameView
  {
    public ThemedSurfaceStyle Style { get; private set; }
    public void Bind(CardViewModel model) => Style = model?.Style;
  }

  public sealed class CardCostBadgeView
  {
    public int Cost { get; private set; }
    public void Bind(CardViewModel model) => Cost = Math.Max(0, model?.Cost ?? 0);
  }

  public sealed class CardStatsView
  {
    public int Attack { get; private set; }
    public int Health { get; private set; }
    public void Bind(CardViewModel model)
    {
      Attack = model?.Attack ?? 0;
      Health = model?.Health ?? 0;
    }
  }

  public sealed class CardRulesTextView
  {
    public string Text { get; private set; } = string.Empty;
    public void Bind(CardViewModel model) => Text = model?.RulesText ?? string.Empty;
  }

  public sealed class CardKeywordBadgeView
  {
    public IReadOnlyList<string> Keywords { get; private set; } = Array.Empty<string>();
    public void Bind(CardViewModel model) => Keywords = model?.Keywords ?? Array.Empty<string>();
  }

  public sealed class CardGlowView
  {
    public bool Visible { get; private set; }
    public string Color { get; private set; } = string.Empty;
    public void Bind(CardViewModel model)
    {
      Visible = model?.Playable == true;
      Color = model?.Style?.AccentColor ?? string.Empty;
    }
  }

  public sealed class CardPlayableHighlightView
  {
    public bool Visible { get; private set; }
    public void Bind(CardViewModel model) => Visible = model?.Playable == true;
  }

  public sealed class CardDisabledOverlayView
  {
    public bool Visible { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public void Bind(CardViewModel model)
    {
      Visible = model?.Disabled == true;
      Reason = model?.DisabledReason ?? string.Empty;
    }
  }

  public sealed class CardViewBinder
  {
    public string LastBoundLabel { get; private set; } = string.Empty;

    public void Bind(CardViewModel model)
    {
      LastBoundLabel = model?.DisplayLabel ?? string.Empty;
    }
  }

  public sealed class CardPlaceholderView
  {
    public CardBackViewModel Model { get; private set; }

    public void Bind(CardBackViewModel model)
    {
      Model = model;
    }
  }

  public sealed class CardInteractionView
  {
    public bool Highlighted { get; private set; }

    public void SetHighlighted(bool highlighted)
    {
      Highlighted = highlighted;
    }
  }
}

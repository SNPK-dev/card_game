using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Unity.Client.UI.Theme;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.UI.Board
{
  public sealed class EntityViewModel
  {
    public string EntityInstanceId { get; set; } = string.Empty;
    public string SourceCardInstanceId { get; set; } = string.Empty;
    public string ControllerPlayerId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int Attack { get; set; }
    public int Health { get; set; }
    public int Damage { get; set; }
    public int RemainingHealth { get; set; }
    public IReadOnlyList<string> Keywords { get; set; } = Array.Empty<string>();
    public string KeywordText { get; set; } = string.Empty;
    public bool IsPlaceholder { get; set; }
    public bool Damaged => Damage > 0;
    public bool TargetCandidate { get; set; }
    public string SortKey => string.IsNullOrWhiteSpace(EntityInstanceId) ? SourceCardInstanceId ?? string.Empty : EntityInstanceId;
    public ThemedSurfaceStyle Style { get; set; } = GameThemeStyles.Entity(GameTheme.CreateDefault(), damaged: false, highlighted: false);
  }

  public sealed class BoardZoneViewModel
  {
    public string PlayerId { get; set; } = string.Empty;
    public bool IsEmpty { get; set; }
    public IReadOnlyList<EntityViewModel> Entities { get; set; } = Array.Empty<EntityViewModel>();
  }

  public sealed class HeroStatusViewModel
  {
    public string PlayerId { get; set; } = string.Empty;
    public EntityViewModel Hero { get; set; }
    public EntityViewModel Weapon { get; set; }
    public EntityViewModel Location { get; set; }
    public string HeroPowerSourceCardInstanceId { get; set; } = string.Empty;
    public int HeroPowerCost { get; set; }
    public string HeroPowerTiming { get; set; } = string.Empty;
  }

  public sealed class ResourceStatusViewModel
  {
    public string PlayerId { get; set; } = string.Empty;
    public int Mana { get; set; }
    public int MaxMana { get; set; }
    public int Armor { get; set; }
    public int FatigueCounter { get; set; }
  }

  public sealed class EntityViewModelFactory
  {
    public EntityViewModel Create(PublicEntityView entity)
    {
      if (entity == null)
      {
        return new EntityViewModel { IsPlaceholder = true };
      }

      var keywords = (entity.Keywords ?? new List<string>())
        .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
        .OrderBy(keyword => keyword, StringComparer.Ordinal)
        .ToList();

      return new EntityViewModel
      {
        EntityInstanceId = entity.EntityInstanceId ?? string.Empty,
        SourceCardInstanceId = entity.SourceCardInstanceId ?? string.Empty,
        ControllerPlayerId = entity.ControllerPlayerId ?? string.Empty,
        EntityType = entity.EntityType ?? string.Empty,
        Attack = entity.Attack,
        Health = entity.Health,
        Damage = entity.Damage,
        RemainingHealth = entity.Health - entity.Damage,
        Keywords = keywords,
        KeywordText = KeywordDisplayFormatter.Format(keywords),
        Style = GameThemeStyles.Entity(GameTheme.CreateDefault(), entity.Damage > 0, highlighted: false)
      };
    }
  }

  public sealed class BoardZoneViewModelFactory
  {
    private readonly EntityViewModelFactory entityFactory = new();

    public BoardZoneViewModel Create(string playerId, IEnumerable<PublicEntityView> entities)
    {
      var mapped = (entities ?? Array.Empty<PublicEntityView>())
        .Select(entityFactory.Create)
        .OrderBy(entity => entity.SortKey, StringComparer.Ordinal)
        .ToList();
      return new BoardZoneViewModel
      {
        PlayerId = playerId ?? string.Empty,
        IsEmpty = mapped.Count == 0,
        Entities = mapped
      };
    }
  }

  public sealed class HeroStatusViewModelFactory
  {
    private readonly EntityViewModelFactory entityFactory = new();

    public HeroStatusViewModel Create(PublicPlayerStateView player) =>
      new()
      {
        PlayerId = player?.PlayerId ?? string.Empty,
        Hero = entityFactory.Create(player?.Hero),
        Weapon = entityFactory.Create(player?.Weapon),
        Location = entityFactory.Create(player?.Location),
        HeroPowerSourceCardInstanceId = player?.HeroPower?.SourceCardInstanceId ?? string.Empty,
        HeroPowerCost = player?.HeroPower?.Cost ?? 0,
        HeroPowerTiming = player?.HeroPower?.ActivationTiming ?? string.Empty
      };
  }

  public sealed class ResourceStatusViewModelFactory
  {
    public ResourceStatusViewModel Create(PublicPlayerStateView player) =>
      new()
      {
        PlayerId = player?.PlayerId ?? string.Empty,
        Mana = player?.Mana ?? 0,
        MaxMana = player?.MaxMana ?? 0,
        Armor = player?.Armor ?? 0,
        FatigueCounter = player?.FatigueCounter ?? 0
      };
  }

  public static class KeywordDisplayFormatter
  {
    public static string Format(IEnumerable<string> keywords) =>
      string.Join(", ", (keywords ?? Array.Empty<string>())
        .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
        .OrderBy(keyword => keyword, StringComparer.Ordinal));
  }

  public sealed class EntityViewBinder
  {
    public EntityViewModel LastBound { get; private set; }

    public void Bind(EntityViewModel model)
    {
      LastBound = model;
    }
  }

  public sealed class BoardZoneView
  {
    public BoardZoneViewModel LastBound { get; private set; }

    public void Bind(BoardZoneViewModel model)
    {
      LastBound = model;
    }
  }

  public sealed class HeroStatusView
  {
    public HeroStatusViewModel LastBound { get; private set; }

    public void Bind(HeroStatusViewModel model)
    {
      LastBound = model;
    }
  }

  public sealed class BoardView
  {
    public BoardZoneViewModel OpponentLane { get; private set; }
    public BoardZoneViewModel PlayerLane { get; private set; }
    public void Bind(BoardZoneViewModel opponentLane, BoardZoneViewModel playerLane)
    {
      OpponentLane = opponentLane;
      PlayerLane = playerLane;
    }
  }

  public sealed class BoardLaneView
  {
    public BoardZoneViewModel LastBound { get; private set; }
    public void Bind(BoardZoneViewModel model) => LastBound = model;
  }

  public sealed class BoardEntityView
  {
    public EntityViewModel LastBound { get; private set; }
    public void Bind(EntityViewModel model) => LastBound = model;
  }

  public sealed class HeroPanelView
  {
    public HeroStatusViewModel LastBound { get; private set; }
    public void Bind(HeroStatusViewModel model) => LastBound = model;
  }

  public sealed class ResourcePanelView
  {
    public ResourceStatusViewModel LastBound { get; private set; }
    public void Bind(ResourceStatusViewModel model) => LastBound = model;
  }

  public sealed class OpponentPanelView
  {
    public HeroStatusViewModel LastBound { get; private set; }
    public void Bind(HeroStatusViewModel model) => LastBound = model;
  }

  public sealed class PlayerPanelView
  {
    public HeroStatusViewModel LastBound { get; private set; }
    public void Bind(HeroStatusViewModel model) => LastBound = model;
  }

  public sealed class DamageBadgeView
  {
    public int Damage { get; private set; }
    public bool Visible { get; private set; }
    public void Bind(EntityViewModel model)
    {
      Damage = Math.Max(0, model?.Damage ?? 0);
      Visible = Damage > 0;
    }
  }

  public sealed class KeywordBadgeView
  {
    public IReadOnlyList<string> Keywords { get; private set; } = Array.Empty<string>();
    public void Bind(EntityViewModel model) => Keywords = model?.Keywords ?? Array.Empty<string>();
  }

  public sealed class TargetRingView
  {
    public bool Visible { get; private set; }
    public string Color { get; private set; } = string.Empty;
    public void Bind(EntityViewModel model)
    {
      Visible = model?.TargetCandidate == true;
      Color = model?.Style?.AccentColor ?? string.Empty;
    }
  }
}

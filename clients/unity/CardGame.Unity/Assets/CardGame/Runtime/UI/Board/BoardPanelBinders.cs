using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Unity.Client.UI.Shell;

namespace CardGame.Unity.Client.UI.Board
{
  public sealed class BoardPanelViewModel
  {
    public BoardZoneViewModel OpponentBoard { get; set; } = new();
    public BoardZoneViewModel PlayerBoard { get; set; } = new();
    public IReadOnlyList<string> HighlightedTargetRefs { get; set; } = Array.Empty<string>();
  }

  public sealed class EntitySurfaceViewModel
  {
    public string RefId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Highlighted { get; set; }
    public bool Clickable { get; set; }
  }

  public sealed class BoardPanelBinder : PanelBinderBase<BoardPanelViewModel>
  {
    public IReadOnlyList<EntitySurfaceViewModel> EntitySurfaces { get; private set; } = Array.Empty<EntitySurfaceViewModel>();

    protected override void OnBind(BoardPanelViewModel model)
    {
      model ??= new BoardPanelViewModel();
      var highlighted = new HashSet<string>(model.HighlightedTargetRefs ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
      EntitySurfaces = Map(model.OpponentBoard, highlighted)
        .Concat(Map(model.PlayerBoard, highlighted))
        .OrderBy(surface => surface.RefId, StringComparer.Ordinal)
        .ToList();
      EnsureText(transform, "BoardPanelText", 13).text = string.Join("\n", EntitySurfaces.Select(surface =>
        $"{(surface.Highlighted ? "[TARGET] " : string.Empty)}{surface.RefId} {surface.Label}"));
    }

    private static IEnumerable<EntitySurfaceViewModel> Map(BoardZoneViewModel zone, HashSet<string> highlighted)
    {
      return (zone?.Entities ?? Array.Empty<EntityViewModel>())
        .OrderBy(entity => entity.EntityInstanceId, StringComparer.Ordinal)
        .Select(entity => new EntitySurfaceViewModel
        {
          RefId = entity.EntityInstanceId ?? string.Empty,
          Label = $"{entity.EntityType} atk:{entity.Attack} hp:{entity.RemainingHealth} dmg:{entity.Damage} {entity.KeywordText}",
          Highlighted = highlighted.Contains(entity.EntityInstanceId ?? string.Empty),
          Clickable = !string.IsNullOrWhiteSpace(entity.EntityInstanceId)
        });
    }
  }

  public sealed class BoardZoneBinder : PanelBinderBase<BoardZoneViewModel>
  {
    public IReadOnlyList<string> BoundEntityRefs { get; private set; } = Array.Empty<string>();

    protected override void OnBind(BoardZoneViewModel model)
    {
      BoundEntityRefs = (model?.Entities ?? Array.Empty<EntityViewModel>())
        .OrderBy(entity => entity.EntityInstanceId, StringComparer.Ordinal)
        .Select(entity => entity.EntityInstanceId)
        .ToList();
      EnsureText(transform, "BoardZoneText", 13).text = string.Join("\n", BoundEntityRefs);
    }
  }

  public sealed class HeroStatusBinder : PanelBinderBase<HeroStatusViewModel>
  {
    protected override void OnBind(HeroStatusViewModel model)
    {
      EnsureText(transform, "HeroStatusText", 13).text =
        $"Hero {model?.PlayerId ?? string.Empty}: {model?.Hero?.EntityInstanceId ?? "none"} HP {model?.Hero?.RemainingHealth ?? 0}";
    }
  }

  public sealed class ResourceStatusBinder : PanelBinderBase<ResourceStatusViewModel>
  {
    protected override void OnBind(ResourceStatusViewModel model)
    {
      EnsureText(transform, "ResourceStatusText", 13).text =
        $"Mana {model?.Mana ?? 0}/{model?.MaxMana ?? 0} Armor {model?.Armor ?? 0} Fatigue {model?.FatigueCounter ?? 0}";
    }
  }
}

using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Unity.Client.UI.Shell;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame.Unity.Client.UI.Cards
{
  public sealed class CardListBinder : PanelBinderBase<IEnumerable<CardViewModel>>
  {
    public IReadOnlyList<string> BoundLabels { get; private set; } = Array.Empty<string>();

    protected override void OnBind(IEnumerable<CardViewModel> model)
    {
      BoundLabels = (model ?? Array.Empty<CardViewModel>()).Select(ToLabel).ToList();
      EnsureText(transform, "CardListText", 13).text = string.Join("\n", BoundLabels);
    }

    private static string ToLabel(CardViewModel model) =>
      model == null ? "missing card" :
      model.IsPlaceholder ? $"{model.Zone}: {model.DisplayLabel}" :
      $"{model.DisplayLabel} [{model.CardType}] {model.Visibility} cost:{model.Cost} {model.Attack}/{model.Health} {model.RulesText}";
  }

  public sealed class CardBackBinder : PanelBinderBase<CardBackViewModel>
  {
    public string BoundLabel { get; private set; } = string.Empty;

    protected override void OnBind(CardBackViewModel model)
    {
      BoundLabel = $"{model?.Zone ?? string.Empty}: {model?.Count ?? 0} hidden";
      EnsureText(transform, "CardBackText", 13).text = BoundLabel;
    }
  }

  public sealed class HandPanelViewModel
  {
    public string PlayerId { get; set; } = string.Empty;
    public bool OwnHand { get; set; }
    public bool SpectatorSafe { get; set; }
    public IReadOnlyList<CardViewModel> Cards { get; set; } = Array.Empty<CardViewModel>();
    public IReadOnlyList<string> PlayableCardRefs { get; set; } = Array.Empty<string>();
  }

  public sealed class HandPanelBinder : PanelBinderBase<HandPanelViewModel>
  {
    public IReadOnlyList<string> BoundCardLabels { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> HighlightedRefs { get; private set; } = Array.Empty<string>();

    protected override void OnBind(HandPanelViewModel model)
    {
      model ??= new HandPanelViewModel();
      var playable = new HashSet<string>(model.PlayableCardRefs ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
      HighlightedRefs = playable.ToList();
      BoundCardLabels = (model.Cards ?? Array.Empty<CardViewModel>())
        .Select(card => ToLabel(card, playable.Contains(card?.CardInstanceId ?? string.Empty)))
        .ToList();
      EnsureText(transform, "HandPanelText", 13).text =
        $"Hand {model.PlayerId} {(model.OwnHand ? "own" : "hidden/count")} {(model.SpectatorSafe ? "spectator-safe" : string.Empty)}\n" +
        string.Join("\n", BoundCardLabels);
    }

    private static string ToLabel(CardViewModel card, bool playable)
    {
      if (card == null)
      {
        return "missing";
      }

      var marker = playable ? " playable" : string.Empty;
      return card.IsPlaceholder
        ? $"[back] {card.DisplayLabel}{marker}"
        : $"[face] {card.DisplayLabel} {card.CardType} cost:{card.Cost} {card.Attack}/{card.Health} {card.CardInstanceId}{marker}";
    }
  }
}

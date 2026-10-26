using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame.Unity.Client.UI.Shell
{
  public abstract class PanelBinderBase<TModel> : MonoBehaviour
  {
    public TModel LastBound { get; private set; }

    public void Bind(TModel model)
    {
      LastBound = model;
      OnBind(model);
    }

    protected abstract void OnBind(TModel model);

    protected static Text EnsureText(Transform parent, string name, int fontSize = 12)
    {
      var existing = parent.Find(name);
      var obj = existing == null ? new GameObject(name) : existing.gameObject;
      obj.transform.SetParent(parent, false);
      var text = obj.GetComponent<Text>() ?? obj.AddComponent<Text>();
      text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
      text.fontSize = fontSize;
      text.color = Color.white;
      text.horizontalOverflow = HorizontalWrapMode.Wrap;
      text.verticalOverflow = VerticalWrapMode.Truncate;
      return text;
    }
  }

  public sealed class GameScreenLayout
  {
    public Transform Root { get; set; }
    public Transform Background { get; set; }
    public Transform Header { get; set; }
    public Transform OpponentPanel { get; set; }
    public Transform OpponentHand { get; set; }
    public Transform Board { get; set; }
    public Transform PlayerPanel { get; set; }
    public Transform PlayerHand { get; set; }
    public Transform InteractionPanel { get; set; }
    public Transform PresentationOverlay { get; set; }
    public Transform ChoiceModal { get; set; }
    public Transform TargetOverlay { get; set; }
    public Transform EventFeed { get; set; }
    public Transform DiagnosticsPanel { get; set; }
    public Transform AudioControls { get; set; }

    public Transform OpponentArea => OpponentPanel;
    public Transform BoardArea => Board;
    public Transform PlayerArea => PlayerPanel;
    public Transform HandArea => PlayerHand;
    public Transform InteractionArea => InteractionPanel;
    public Transform PresentationArea => PresentationOverlay;
    public Transform Diagnostics => DiagnosticsPanel;

    public IReadOnlyList<string> RegionNames => new[]
    {
      Background?.name,
      Header?.name,
      OpponentPanel?.name,
      OpponentHand?.name,
      Board?.name,
      PlayerPanel?.name,
      PlayerHand?.name,
      InteractionPanel?.name,
      PresentationOverlay?.name,
      ChoiceModal?.name,
      TargetOverlay?.name,
      EventFeed?.name,
      DiagnosticsPanel?.name,
      AudioControls?.name
    };
  }

  public sealed class GameScreenLayoutBuilder
  {
    public GameScreenLayout Build(Transform parent)
    {
      var root = CreatePanel(parent, "GameplayScreen", new Color(0.07f, 0.08f, 0.09f, 0.98f));
      var rootLayout = root.AddComponent<VerticalLayoutGroup>();
      rootLayout.padding = new RectOffset(10, 10, 10, 10);
      rootLayout.spacing = 6;
      rootLayout.childControlHeight = true;
      rootLayout.childControlWidth = true;

      return new GameScreenLayout
      {
        Root = root.transform,
        Background = CreateRow(root.transform, "Background", 24).transform,
        Header = CreateRow(root.transform, "Header", 44).transform,
        OpponentPanel = CreateRow(root.transform, "OpponentPanel", 72).transform,
        OpponentHand = CreateRow(root.transform, "OpponentHand", 62).transform,
        Board = CreateRow(root.transform, "Board", 134).transform,
        PlayerPanel = CreateRow(root.transform, "PlayerPanel", 72).transform,
        PlayerHand = CreateRow(root.transform, "PlayerHand", 132).transform,
        InteractionPanel = CreateRow(root.transform, "InteractionPanel", 96).transform,
        PresentationOverlay = CreateRow(root.transform, "PresentationOverlay", 58).transform,
        ChoiceModal = CreateRow(root.transform, "ChoiceModal", 72).transform,
        TargetOverlay = CreateRow(root.transform, "TargetOverlay", 52).transform,
        EventFeed = CreateRow(root.transform, "EventFeed", 104).transform,
        DiagnosticsPanel = CreateRow(root.transform, "DiagnosticsPanel", 70).transform,
        AudioControls = CreateRow(root.transform, "AudioControls", 44).transform
      };
    }

    public static GameObject CreateRow(Transform parent, string name, int height)
    {
      var row = CreatePanel(parent, name, new Color(0.11f, 0.12f, 0.14f, 0.96f));
      var layout = row.AddComponent<HorizontalLayoutGroup>();
      layout.spacing = 6;
      layout.padding = new RectOffset(6, 6, 5, 5);
      layout.childControlWidth = true;
      layout.childControlHeight = true;
      layout.childForceExpandWidth = false;
      var element = row.AddComponent<LayoutElement>();
      element.minHeight = height;
      element.preferredHeight = height;
      return row;
    }

    public static GameObject CreatePanel(Transform parent, string name, Color color)
    {
      var obj = new GameObject(name);
      obj.transform.SetParent(parent, false);
      obj.AddComponent<RectTransform>();
      obj.AddComponent<Image>().color = color;
      return obj;
    }
  }

  public sealed class GameShellPresenter
  {
    public GameShellView LastShell { get; private set; }
    public GameplayScreenStateViewModel LastState { get; private set; }
    public IReadOnlyList<string> LastDiagnostics { get; private set; } = Array.Empty<string>();

    public void Present(GameShellView shell, GameplayScreenStateViewModel state, IEnumerable<string> diagnostics)
    {
      LastShell = shell;
      LastState = state;
      LastDiagnostics = (diagnostics ?? Array.Empty<string>())
        .Where(line => !string.IsNullOrWhiteSpace(line))
        .ToList();
    }
  }

  public sealed class GameShellBinder : PanelBinderBase<GameShellView>
  {
    private Text label;

    protected override void OnBind(GameShellView model)
    {
      label ??= EnsureText(transform, "ShellSummary", 12);
      label.text = model == null
        ? "Shell not bound."
        : $"Match {model.MatchId} | View {model.ViewKind} | Viewer {model.ViewerPlayerId} | Commands {(model.CommandStatus.CommandsEnabled ? "enabled" : "disabled")}";
    }
  }

  public sealed class DiagnosticsPanelBinder : PanelBinderBase<IEnumerable<string>>
  {
    private Text label;
    public string LastText { get; private set; } = string.Empty;

    protected override void OnBind(IEnumerable<string> model)
    {
      label ??= EnsureText(transform, "DiagnosticsText", 12);
      LastText = string.Join("\n", (model ?? Array.Empty<string>()).Where(line => !string.IsNullOrWhiteSpace(line)));
      label.text = string.IsNullOrWhiteSpace(LastText) ? "Diagnostics\nNone" : "Diagnostics\n" + LastText;
    }
  }
}

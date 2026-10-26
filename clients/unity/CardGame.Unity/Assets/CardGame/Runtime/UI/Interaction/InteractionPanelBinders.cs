using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Unity.Client.UI.Shell;
using UnityEngine.UI;

namespace CardGame.Unity.Client.UI.Interaction
{
  public sealed class CommandButtonViewModel
  {
    public string Label { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string DisabledReason { get; set; } = string.Empty;
    public string ExpectedPreStateHashShort { get; set; } = string.Empty;
  }

  public sealed class CommandButtonBinder : PanelBinderBase<CommandIntentViewModel>
  {
    public CommandButtonViewModel Button { get; private set; } = new();

    protected override void OnBind(CommandIntentViewModel model)
    {
      Button = new CommandButtonViewModel
      {
        Label = model?.CommandType ?? string.Empty,
        Enabled = model?.Enabled == true,
        DisabledReason = model?.DisabledReason ?? string.Empty,
        ExpectedPreStateHashShort = ShortHash(model?.ExpectedPreStateHash)
      };
      var uiButton = GetComponent<Button>() ?? gameObject.AddComponent<Button>();
      uiButton.interactable = Button.Enabled;
      EnsureText(transform, "CommandButtonText", 13).text =
        $"{Button.Label}\n{Button.ExpectedPreStateHashShort}\n{Button.DisabledReason}";
    }

    private static string ShortHash(string hash) =>
      string.IsNullOrWhiteSpace(hash) ? string.Empty : hash.Substring(0, Math.Min(8, hash.Length));
  }

  public sealed class LegalActionPanelBinder : PanelBinderBase<LegalActionPanelViewModel>
  {
    public IReadOnlyList<CommandButtonViewModel> Buttons { get; private set; } = Array.Empty<CommandButtonViewModel>();

    protected override void OnBind(LegalActionPanelViewModel model)
    {
      Buttons = (model?.Commands ?? Array.Empty<CommandIntentViewModel>())
        .OrderBy(command => command.CommandType, StringComparer.Ordinal)
        .Select(command => new CommandButtonViewModel
        {
          Label = command.CommandType,
          Enabled = command.Enabled,
          DisabledReason = command.DisabledReason,
          ExpectedPreStateHashShort = string.IsNullOrWhiteSpace(command.ExpectedPreStateHash) ? string.Empty : command.ExpectedPreStateHash.Substring(0, Math.Min(8, command.ExpectedPreStateHash.Length))
        })
        .ToList();
      EnsureText(transform, "LegalActionPanelText", 13).text = string.Join("\n", Buttons.Select(button =>
        $"{button.Label} {(button.Enabled ? "[ready]" : "[disabled]")} hash:{button.ExpectedPreStateHashShort} {button.DisabledReason}"));
    }
  }

  public sealed class TargetSelectionPanelBinder : PanelBinderBase<TargetSelectionViewModel>
  {
    public IReadOnlyList<string> HighlightedRefs { get; private set; } = Array.Empty<string>();

    protected override void OnBind(TargetSelectionViewModel model)
    {
      HighlightedRefs = (model?.Candidates ?? Array.Empty<TargetCandidateViewModel>())
        .Where(candidate => candidate.Highlighted)
        .Select(candidate => candidate.RefId)
        .ToList();
      EnsureText(transform, "TargetSelectionText", 13).text =
        $"TARGET: {model?.Banner ?? string.Empty}\nHighlighted {HighlightedRefs.Count}: {string.Join(", ", HighlightedRefs)}\n{model?.Diagnostic ?? string.Empty}";
    }
  }

  public sealed class ChoicePanelBinder : PanelBinderBase<ChoicePanelViewModel>
  {
    public IReadOnlyList<string> EnabledOptionIds { get; private set; } = Array.Empty<string>();

    protected override void OnBind(ChoicePanelViewModel model)
    {
      EnabledOptionIds = (model?.Options ?? Array.Empty<ChoiceOptionViewModel>())
        .Where(option => option.Enabled)
        .Select(option => option.OptionId)
        .ToList();
      EnsureText(transform, "ChoicePanelText", 13).text =
        $"Choice {model?.ChoiceId ?? string.Empty} {model?.ChoiceType ?? string.Empty} {model?.SourceRef ?? string.Empty}->{model?.ResultZone ?? string.Empty}\n" +
        string.Join("\n", (model?.Options ?? Array.Empty<ChoiceOptionViewModel>()).Select(option =>
          $"{option.OptionId} {option.Label} {option.Visibility} {(option.Enabled ? "enabled" : "disabled")}"));
    }
  }
}

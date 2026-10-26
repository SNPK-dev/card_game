using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Unity.Client.Match;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.UI.Interaction
{
  public enum CommandAvailabilityReason
  {
    None,
    Booting,
    Reconnecting,
    SubmittingCommand,
    ResolvingChoice,
    SpectatorMode,
    Terminal,
    Faulted,
    MissingLegalAction,
    StaleHash
  }

  public sealed class CommandIntentViewModel
  {
    public string CommandType { get; set; } = string.Empty;
    public MatchCommandDto Template { get; set; }
    public bool Enabled { get; set; }
    public string DisabledReason { get; set; } = string.Empty;
    public string ExpectedPreStateHash { get; set; } = string.Empty;
    public bool Submitting { get; set; }
    public bool Reconnecting { get; set; }
    public bool Spectator { get; set; }
    public bool Terminal { get; set; }
    public string VisualState =>
      Enabled ? "enabled" :
      Submitting ? "submitting" :
      Reconnecting ? "reconnecting" :
      Spectator ? "spectator" :
      Terminal ? "terminal" :
      "disabled";
  }

  public sealed class LegalActionPanelViewModel
  {
    public IReadOnlyList<CommandIntentViewModel> Commands { get; set; } = Array.Empty<CommandIntentViewModel>();
    public IReadOnlyList<CommandGroupViewModel> Groups { get; set; } = Array.Empty<CommandGroupViewModel>();
  }

  public sealed class CommandGroupViewModel
  {
    public string CommandType { get; set; } = string.Empty;
    public IReadOnlyList<CommandIntentViewModel> Commands { get; set; } = Array.Empty<CommandIntentViewModel>();
  }

  public sealed class LegalActionViewModelFactory
  {
    public LegalActionPanelViewModel Create(
      ExternalLegalActionsResult legalActions,
      CommandSubmitGuard guard,
      string expectedPreStateHash)
    {
      var actions = legalActions?.LegalActions ?? new List<MatchCommandDto>();
      return new LegalActionPanelViewModel
      {
        Commands = actions
          .OrderBy(action => action.CommandType ?? string.Empty, StringComparer.OrdinalIgnoreCase)
          .Select(action => guard.CreateIntent(action, expectedPreStateHash))
          .ToList(),
        Groups = actions
          .OrderBy(action => action.CommandType ?? string.Empty, StringComparer.OrdinalIgnoreCase)
          .GroupBy(action => action.CommandType ?? string.Empty, StringComparer.OrdinalIgnoreCase)
          .Select(group => new CommandGroupViewModel
          {
            CommandType = group.Key,
            Commands = group.Select(action => guard.CreateIntent(action, expectedPreStateHash)).ToList()
          })
          .ToList()
      };
    }
  }

  public sealed class CommandSubmitGuard
  {
    private readonly IReadOnlyList<CommandAvailabilityReason> reasons;

    public CommandSubmitGuard(params CommandAvailabilityReason[] reasons)
    {
      this.reasons = reasons?.Where(reason => reason != CommandAvailabilityReason.None).ToList() ??
        new List<CommandAvailabilityReason>();
    }

    public bool CanSubmit => reasons.Count == 0;
    public string DisabledReason => string.Join("; ", reasons.Select(ToMessage));

    public CommandIntentViewModel CreateIntent(MatchCommandDto template, string expectedPreStateHash)
    {
      var missing = template == null ? CommandAvailabilityReason.MissingLegalAction : CommandAvailabilityReason.None;
      var enabled = CanSubmit && missing == CommandAvailabilityReason.None;
      var reason = missing == CommandAvailabilityReason.None ? DisabledReason : ToMessage(missing);
      return new CommandIntentViewModel
      {
        CommandType = template?.CommandType ?? string.Empty,
        Template = template,
        Enabled = enabled,
        DisabledReason = enabled ? string.Empty : reason,
        ExpectedPreStateHash = expectedPreStateHash ?? string.Empty
      };
    }

    public bool RejectsLocalCommand(MatchCommandDto template) => template == null;

    public static string ToMessage(CommandAvailabilityReason reason) =>
      reason switch
      {
        CommandAvailabilityReason.Booting => "Client is booting.",
        CommandAvailabilityReason.Reconnecting => "Reconnecting to backend.",
        CommandAvailabilityReason.SubmittingCommand => "A command is already being submitted.",
        CommandAvailabilityReason.ResolvingChoice => "A choice is being resolved.",
        CommandAvailabilityReason.SpectatorMode => "Spectator mode cannot submit commands.",
        CommandAvailabilityReason.Terminal => "Match is terminal.",
        CommandAvailabilityReason.Faulted => "Match is faulted.",
        CommandAvailabilityReason.MissingLegalAction => "A server-provided legal action is required.",
        CommandAvailabilityReason.StaleHash => "State hash is stale; reconnect and refresh legal actions.",
        _ => string.Empty
      };
  }

  public sealed class TargetCandidateViewModel
  {
    public string RefId { get; set; } = string.Empty;
    public bool Highlighted { get; set; }
  }

  public sealed class TargetSelectionViewModel
  {
    public bool Active { get; set; }
    public string Banner { get; set; } = string.Empty;
    public string Diagnostic { get; set; } = string.Empty;
    public bool CanCancel { get; set; }
    public IReadOnlyList<TargetCandidateViewModel> Candidates { get; set; } = Array.Empty<TargetCandidateViewModel>();
    public int CandidateCount => Candidates?.Count ?? 0;
  }

  public sealed class TargetSelectionViewModelFactory
  {
    public TargetSelectionViewModel Create(TargetSelectionState state, string lastDiagnostic = "")
    {
      state ??= TargetSelectionState.Inactive;
      return new TargetSelectionViewModel
      {
        Active = state.IsActive,
        Banner = state.IsActive ? "Select a highlighted target." : string.Empty,
        Diagnostic = lastDiagnostic ?? string.Empty,
        CanCancel = state.IsActive,
        Candidates = state.CandidateTargetRefs
          .Select(refId => new TargetCandidateViewModel { RefId = refId, Highlighted = true })
          .ToList()
      };
    }

    public string NonCandidateDiagnostic(string refId) =>
      $"Target '{refId}' is not a server-provided candidate.";
  }

  public sealed class TargetSelectionBanner
  {
    public string Text { get; set; } = "Select a highlighted target.";
  }

  public sealed class TargetCancelAction
  {
    public bool CanCancel { get; set; }
  }

  public sealed class ChoiceOptionViewModel
  {
    public string OptionId { get; set; } = string.Empty;
    public string ItemKind { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string Visibility { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Enabled { get; set; }
  }

  public sealed class ChoicePanelViewModel
  {
    public string ChoiceId { get; set; } = string.Empty;
    public string ChoiceType { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public string ResultZone { get; set; } = string.Empty;
    public string VisibilityPolicy { get; set; } = string.Empty;
    public IReadOnlyList<ChoiceOptionViewModel> Options { get; set; } = Array.Empty<ChoiceOptionViewModel>();
  }

  public sealed class ChoiceViewModelFactory
  {
    public IReadOnlyList<ChoicePanelViewModel> CreateVisible(MatchStateView state)
    {
      return (state?.PendingChoices ?? new Dictionary<string, PublicChoiceView>())
        .Values
        .Where(choice => !string.IsNullOrWhiteSpace(choice?.ChoiceId))
        .OrderBy(choice => choice.ChoiceId, StringComparer.Ordinal)
        .Select(ToPanel)
        .ToList();
    }

    private static ChoicePanelViewModel ToPanel(PublicChoiceView choice) =>
      new()
      {
        ChoiceId = choice.ChoiceId ?? string.Empty,
        ChoiceType = choice.ChoiceType ?? string.Empty,
        SourceRef = choice.SourceRef ?? string.Empty,
        ResultZone = choice.ResultZone ?? string.Empty,
        VisibilityPolicy = choice.VisibilityPolicy ?? string.Empty,
        Options = (choice.Options ?? new List<PublicChoiceOptionView>())
          .Select(option => new ChoiceOptionViewModel
          {
            OptionId = option.OptionId ?? string.Empty,
            ItemKind = option.ItemKind ?? string.Empty,
            ItemId = option.ItemId ?? string.Empty,
            Visibility = option.Visibility ?? string.Empty,
            Label = string.IsNullOrWhiteSpace(option.ItemId) ? option.ItemKind ?? string.Empty : option.ItemId,
            Enabled = !string.IsNullOrWhiteSpace(option.OptionId)
          })
          .ToList()
      };
  }

  public sealed class ChoiceResolveGuard
  {
    public bool CanResolve(ChoicePanelViewModel choice, ChoiceOptionViewModel option) =>
      !string.IsNullOrWhiteSpace(choice?.ChoiceId) &&
      !string.IsNullOrWhiteSpace(option?.OptionId);

    public MatchCommandDto BuildResolveTemplate(string playerId, ChoicePanelViewModel choice, ChoiceOptionViewModel option)
    {
      if (!CanResolve(choice, option))
      {
        return null;
      }

      return new MatchCommandDto
      {
        CommandType = "RESOLVE_CHOICE",
        PlayerId = playerId ?? string.Empty,
        Refs = new Dictionary<string, string>
        {
          ["choiceId"] = choice.ChoiceId,
          ["selectedOptionId"] = option.OptionId
        }
      };
    }
  }

  public sealed class InteractionStateView
  {
    public TargetSelectionViewModel TargetSelection { get; private set; }
    public ChoicePanelViewModel Choice { get; private set; }
    public void Bind(TargetSelectionViewModel model) => TargetSelection = model;
    public void Bind(ChoicePanelViewModel model) => Choice = model;
  }

  public sealed class TargetSelectionView
  {
    public TargetSelectionViewModel LastBound { get; private set; }
    public void Bind(TargetSelectionViewModel model) => LastBound = model;
  }

  public sealed class ChoicePanelView
  {
    public ChoicePanelViewModel LastBound { get; private set; }
    public void Bind(ChoicePanelViewModel model) => LastBound = model;
  }

  public sealed class LegalActionPanelView
  {
    public LegalActionPanelViewModel LastBound { get; private set; }
    public void Bind(LegalActionPanelViewModel model) => LastBound = model;
  }

  public sealed class CommandButtonView
  {
    public CommandIntentViewModel LastBound { get; private set; }
    public string State { get; private set; } = string.Empty;
    public void Bind(CommandIntentViewModel model)
    {
      LastBound = model;
      State = model?.VisualState ?? "disabled";
    }
  }

  public sealed class CommandGroupView
  {
    public CommandGroupViewModel LastBound { get; private set; }
    public void Bind(CommandGroupViewModel model) => LastBound = model;
  }

  public sealed class TargetSelectionOverlay
  {
    public TargetSelectionViewModel LastBound { get; private set; }
    public void Bind(TargetSelectionViewModel model) => LastBound = model;
  }

  public sealed class ChoiceModalView
  {
    public ChoicePanelViewModel LastBound { get; private set; }
    public void Bind(ChoicePanelViewModel model) => LastBound = model;
  }

  public sealed class ChoiceOptionView
  {
    public ChoiceOptionViewModel LastBound { get; private set; }
    public void Bind(ChoiceOptionViewModel model) => LastBound = model;
  }

  public sealed class ReconnectButtonView
  {
    public bool Enabled { get; private set; }
    public string Label { get; private set; } = "Reconnect";
    public void Bind(bool enabled, bool reconnecting)
    {
      Enabled = enabled && !reconnecting;
      Label = reconnecting ? "Reconnecting" : "Reconnect";
    }
  }

  public sealed class ReplayVerifyButtonView
  {
    public bool Enabled { get; private set; }
    public string State { get; private set; } = string.Empty;
    public void Bind(bool enabled, bool mismatch)
    {
      Enabled = enabled && !mismatch;
      State = mismatch ? "blocking-mismatch" : enabled ? "ready" : "disabled";
    }
  }

  public sealed class ToastView
  {
    public string Message { get; private set; } = string.Empty;
    public string Severity { get; private set; } = string.Empty;
    public void Show(string message, string severity = "info")
    {
      Message = message ?? string.Empty;
      Severity = severity ?? "info";
    }
  }
}

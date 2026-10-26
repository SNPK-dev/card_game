using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Unity.Client.Match;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.UI.Shell
{
  public sealed class GameShellView
  {
    public string MatchId { get; set; } = string.Empty;
    public string ViewKind { get; set; } = string.Empty;
    public string ViewerPlayerId { get; set; } = string.Empty;
    public List<PlayerStatusView> Players { get; set; } = new();
    public List<BoardZoneView> BoardZones { get; set; } = new();
    public List<HandZoneView> HandZones { get; set; } = new();
    public ModeStatusView ModeStatus { get; set; } = new();
    public InteractionStateView InteractionState { get; set; } = new();
    public CommandStatusView CommandStatus { get; set; } = new();
    public ConnectionStatusView ConnectionStatus { get; set; } = new();
    public ReplayStatusView ReplayStatus { get; set; } = new();
  }

  public sealed class GameShellViewModel
  {
    public GameShellView View { get; set; } = new();
  }

  public sealed class GameShellState
  {
    public string UiState { get; set; } = "Ready";
    public ClientViewMode Mode { get; set; }
    public bool IsBooting { get; set; }
    public bool IsSubmittingCommand { get; set; }
    public bool IsResolvingChoice { get; set; }
    public bool IsReconnecting { get; set; }
    public bool IsTerminal { get; set; }
    public bool IsFaulted { get; set; }
  }

  public sealed class GameShellController
  {
    public GameShellView LastView { get; private set; }

    public void Bind(GameShellView view)
    {
      LastView = view;
    }
  }

  public sealed class CardViewModel
  {
    public string CardInstanceId { get; set; } = string.Empty;
    public string CardId { get; set; } = string.Empty;
    public string CardVersion { get; set; } = string.Empty;
    public string OwnerPlayerId { get; set; } = string.Empty;
    public string CardType { get; set; } = string.Empty;
    public bool IsHiddenPlaceholder { get; set; }
    public int Count { get; set; } = 1;
  }

  public sealed class EntityViewModel
  {
    public string EntityInstanceId { get; set; } = string.Empty;
    public string SourceCardInstanceId { get; set; } = string.Empty;
    public string ControllerPlayerId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int Attack { get; set; }
    public int Health { get; set; }
    public int Damage { get; set; }
    public List<string> Keywords { get; set; } = new();
  }

  public sealed class BoardZoneView
  {
    public string PlayerId { get; set; } = string.Empty;
    public List<EntityViewModel> Entities { get; set; } = new();
  }

  public sealed class HandZoneView
  {
    public string PlayerId { get; set; } = string.Empty;
    public bool IsViewerHand { get; set; }
    public List<CardViewModel> Cards { get; set; } = new();
  }

  public class PlayerStatusView
  {
    public string PlayerId { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public int Mana { get; set; }
    public int MaxMana { get; set; }
    public int Armor { get; set; }
    public int FatigueCounter { get; set; }
  }

  public sealed class OpponentStatusView : PlayerStatusView
  {
  }

  public sealed class ModeStatusView
  {
    public ClientViewMode Mode { get; set; }
    public string ProjectionPlayerId { get; set; } = string.Empty;
    public bool IsDebugOnly { get; set; }
    public string Warning { get; set; } = string.Empty;
  }

  public sealed class InteractionStateView
  {
    public bool IsSelectingTarget { get; set; }
    public List<string> HighlightedRefs { get; set; } = new();
    public bool CanCancel { get; set; }
  }

  public sealed class CommandStatusView
  {
    public bool CommandsEnabled { get; set; }
    public List<string> DisabledReasons { get; set; } = new();
  }

  public sealed class ConnectionStatusView
  {
    public bool IsReconnecting { get; set; }
    public string Message { get; set; } = string.Empty;
  }

  public sealed class ReplayStatusView
  {
    public bool IsVerified { get; set; }
    public bool IsBlocking { get; set; }
    public string Message { get; set; } = string.Empty;
  }

  public static class GameShellViewModelBuilder
  {
    public static GameShellView Build(
      MatchStateView state,
      ClientViewMode mode,
      TargetSelectionState targetSelection,
      bool commandsEnabled,
      IEnumerable<string> disabledReasons,
      bool isReconnecting,
      bool replayVerified,
      bool replayMismatch)
    {
      state ??= new MatchStateView();
      targetSelection ??= TargetSelectionState.Inactive;
      var view = new GameShellView
      {
        MatchId = state.MatchId,
        ViewKind = state.ViewKind,
        ViewerPlayerId = state.ViewerPlayerId ?? string.Empty,
        ModeStatus = new ModeStatusView
        {
          Mode = mode,
          ProjectionPlayerId = state.ViewerPlayerId ?? string.Empty,
          IsDebugOnly = mode == ClientViewMode.DebugBoth,
          Warning = mode == ClientViewMode.DebugBoth ? ClientModeState.DebugBothWarning : string.Empty
        },
        InteractionState = new InteractionStateView
        {
          IsSelectingTarget = targetSelection.IsActive,
          HighlightedRefs = targetSelection.CandidateTargetRefs.ToList(),
          CanCancel = targetSelection.IsActive
        },
        CommandStatus = new CommandStatusView
        {
          CommandsEnabled = commandsEnabled && !isReconnecting,
          DisabledReasons = disabledReasons?.Where(reason => !string.IsNullOrWhiteSpace(reason)).ToList() ?? new List<string>()
        },
        ConnectionStatus = new ConnectionStatusView
        {
          IsReconnecting = isReconnecting,
          Message = isReconnecting ? "Reconnecting" : "Connected"
        },
        ReplayStatus = new ReplayStatusView
        {
          IsVerified = replayVerified,
          IsBlocking = replayMismatch,
          Message = replayMismatch ? "Replay verification mismatch" : replayVerified ? "Replay verified" : "Replay not verified"
        }
      };

      foreach (var pair in state.Players.OrderBy(pair => pair.Key, StringComparer.Ordinal))
      {
        var player = pair.Value;
        view.Players.Add(new PlayerStatusView
        {
          PlayerId = player.PlayerId,
          Relationship = player.Relationship,
          Mana = player.Mana,
          MaxMana = player.MaxMana,
          Armor = player.Armor,
          FatigueCounter = player.FatigueCounter
        });
        view.BoardZones.Add(new BoardZoneView
        {
          PlayerId = player.PlayerId,
          Entities = (player.Zones?.Board ?? new List<PublicEntityView>()).Select(ToEntity).ToList()
        });
        view.HandZones.Add(new HandZoneView
        {
          PlayerId = player.PlayerId,
          IsViewerHand = string.Equals(player.Relationship, "SELF", StringComparison.OrdinalIgnoreCase),
          Cards = ToHandCards(player.Zones?.Hand, player.Relationship, state.ViewKind)
        });
      }

      return view;
    }

    public static CardViewModel ToCard(PublicCardInstanceView card, bool forceHidden)
    {
      if (card == null || forceHidden || string.Equals(card.Visibility, "HIDDEN", StringComparison.OrdinalIgnoreCase))
      {
        return new CardViewModel { IsHiddenPlaceholder = true };
      }

      return new CardViewModel
      {
        CardInstanceId = card.CardInstanceId ?? string.Empty,
        CardId = card.CardId ?? string.Empty,
        CardVersion = card.CardVersion ?? string.Empty,
        OwnerPlayerId = card.OwnerPlayerId,
        CardType = card.CardType,
        IsHiddenPlaceholder = false
      };
    }

    public static EntityViewModel ToEntity(PublicEntityView entity) =>
      new()
      {
        EntityInstanceId = entity?.EntityInstanceId ?? string.Empty,
        SourceCardInstanceId = entity?.SourceCardInstanceId ?? string.Empty,
        ControllerPlayerId = entity?.ControllerPlayerId ?? string.Empty,
        EntityType = entity?.EntityType ?? string.Empty,
        Attack = entity?.Attack ?? 0,
        Health = entity?.Health ?? 0,
        Damage = entity?.Damage ?? 0,
        Keywords = entity?.Keywords?.ToList() ?? new List<string>()
      };

    private static List<CardViewModel> ToHandCards(PublicZoneView hand, string relationship, string viewKind)
    {
      var hideIdentities = !string.Equals(relationship, "SELF", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(viewKind, "SPECTATOR", StringComparison.OrdinalIgnoreCase);
      if (hideIdentities)
      {
        return new List<CardViewModel>
        {
          new() { IsHiddenPlaceholder = true, Count = Math.Max(hand?.Count ?? 0, hand?.Cards?.Count ?? 0) }
        };
      }

      return (hand?.Cards ?? new List<PublicCardInstanceView>())
        .Select(card => ToCard(card, forceHidden: false))
        .ToList();
    }
  }
}

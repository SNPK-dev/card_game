using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Unity.Client.Api;
using CardGame.Unity.Client.Config;
using CardGame.Unity.Client.Match;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.UI.Status
{
  public class PlayerStatusViewModel
  {
    public string PlayerId { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public int Mana { get; set; }
    public int MaxMana { get; set; }
    public int Armor { get; set; }
    public int FatigueCounter { get; set; }
  }

  public sealed class OpponentStatusViewModel : PlayerStatusViewModel
  {
  }

  public sealed class SpectatorStatusViewModel
  {
    public bool CommandsDisabled { get; set; } = true;
    public string Message { get; set; } = "Spectator projection is read-only.";
  }

  public sealed class ModeStatusViewModel
  {
    public ClientViewMode Mode { get; set; }
    public string ProjectionPlayerId { get; set; } = string.Empty;
    public bool IsDebugOnly { get; set; }
    public string Warning { get; set; } = string.Empty;

    public static ModeStatusViewModel From(ClientViewMode mode, string projectionPlayerId) =>
      new()
      {
        Mode = mode,
        ProjectionPlayerId = projectionPlayerId ?? string.Empty,
        IsDebugOnly = mode == ClientViewMode.DebugBoth,
        Warning = mode == ClientViewMode.DebugBoth ? ClientModeState.DebugBothWarning : string.Empty
      };
  }

  public sealed class CommandStatusViewModel
  {
    public bool Enabled { get; set; }
    public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
    public string ExpectedPreStateHash { get; set; } = string.Empty;
  }

  public sealed class ConnectionStatusViewModel
  {
    public string BackendUrl { get; set; } = string.Empty;
    public bool BackendHealthy { get; set; }
    public bool IsReconnecting { get; set; }
    public string Message { get; set; } = string.Empty;
  }

  public sealed class ReconnectViewModel
  {
    public bool Succeeded { get; set; }
    public string StateHash { get; set; } = string.Empty;
    public long EventCursor { get; set; }
    public int MissedEventCount { get; set; }

    public static ReconnectViewModel From(MatchReconnectResult result) =>
      new()
      {
        Succeeded = result != null,
        StateHash = result?.State?.StateHash ?? string.Empty,
        EventCursor = result?.NextEventSeq ?? 0,
        MissedEventCount = result?.Events?.Count ?? 0
      };
  }

  public sealed class ReplayVerificationViewModel
  {
    public bool Verified { get; set; }
    public bool Blocking { get; set; }
    public string FinalStateHash { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public static ReplayVerificationViewModel From(MatchReplayVerificationView replay) =>
      new()
      {
        Verified = replay?.Replay?.IsVerified == true,
        Blocking = replay != null && replay.Replay?.IsVerified == false,
        FinalStateHash = replay?.Replay?.FinalStateHash ?? string.Empty,
        Message = replay?.Replay?.IsVerified == true ? "Replay verified" : "Replay mismatch"
      };
  }

  public sealed class BackendHealthViewModel
  {
    public string BackendUrl { get; set; } = string.Empty;
    public bool Available { get; set; }
    public bool Recoverable { get; set; }
    public string Message { get; set; } = string.Empty;

    public static BackendHealthViewModel FromResult(UnityBackendConfig config, bool available, string message) =>
      new()
      {
        BackendUrl = config?.BackendBaseUrl ?? string.Empty,
        Available = available,
        Recoverable = !available,
        Message = string.IsNullOrWhiteSpace(message) ? (available ? "Backend reachable" : "Backend unavailable") : message
      };
  }

  public sealed class CapabilityStatusViewModel
  {
    public bool Supported { get; set; }
    public string ApiContractVersion { get; set; } = string.Empty;
    public string StateSchemaVersion { get; set; } = string.Empty;
    public string EventSchemaVersion { get; set; } = string.Empty;
    public IReadOnlyList<string> MissingCapabilities { get; set; } = Array.Empty<string>();
    public bool BlocksBoot => !Supported;

    public static CapabilityStatusViewModel From(EngineCapabilityRegistry registry, CapabilityCheckResult result) =>
      new()
      {
        Supported = result?.Supported == true,
        ApiContractVersion = registry?.ApiContractVersion ?? string.Empty,
        StateSchemaVersion = registry?.StateSchemaVersion ?? string.Empty,
        EventSchemaVersion = registry?.EventSchemaVersion ?? string.Empty,
        MissingCapabilities = result?.MissingCapabilities?.ToList() ?? new List<string>()
      };
  }

  public sealed class ErrorStatusViewModel
  {
    public bool HasError { get; set; }
    public bool Blocking { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public static ErrorStatusViewModel From(UnityError error, bool blocking) =>
      new()
      {
        HasError = error != null,
        Blocking = blocking,
        Code = error?.Code ?? string.Empty,
        Message = error?.Message ?? string.Empty
      };
  }

  public sealed class PlayerStatusView
  {
    public PlayerStatusViewModel LastBound { get; private set; }
    public void Bind(PlayerStatusViewModel model) => LastBound = model;
  }

  public sealed class OpponentStatusView
  {
    public OpponentStatusViewModel LastBound { get; private set; }
    public void Bind(OpponentStatusViewModel model) => LastBound = model;
  }

  public sealed class SpectatorStatusView
  {
    public SpectatorStatusViewModel LastBound { get; private set; }
    public void Bind(SpectatorStatusViewModel model) => LastBound = model;
  }

  public sealed class ModeStatusView
  {
    public ModeStatusViewModel LastBound { get; private set; }
    public void Bind(ModeStatusViewModel model) => LastBound = model;
  }

  public sealed class CommandStatusView
  {
    public CommandStatusViewModel LastBound { get; private set; }
    public void Bind(CommandStatusViewModel model) => LastBound = model;
  }

  public sealed class ConnectionStatusView
  {
    public ConnectionStatusViewModel LastBound { get; private set; }
    public void Bind(ConnectionStatusViewModel model) => LastBound = model;
  }

  public sealed class ReplayStatusView
  {
    public ReplayVerificationViewModel LastBound { get; private set; }
    public void Bind(ReplayVerificationViewModel model) => LastBound = model;
  }

  public sealed class ErrorStatusView
  {
    public ErrorStatusViewModel LastBound { get; private set; }
    public void Bind(ErrorStatusViewModel model) => LastBound = model;
  }
}

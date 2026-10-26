using System;
using CardGame.Unity.Client.Match;

namespace CardGame.Unity.Client.UI.Shell
{
  public enum GameplayScreenState
  {
    Booting,
    BackendUnavailable,
    ReadyToSeed,
    ReadyToStart,
    StartingMatch,
    MatchActive,
    SelectingTarget,
    ResolvingChoice,
    SubmittingCommand,
    Reconnecting,
    ReplayVerifying,
    Terminal,
    Faulted,
    Spectating,
    DebugProjectionSwitch
  }

  public sealed class GameplayScreenStateViewModel
  {
    public GameplayScreenState State { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public bool Blocking { get; set; }
    public bool Recoverable { get; set; }
    public bool CommandsEnabled { get; set; }
    public bool ResetsTargetSelection { get; set; }
    public string StateHash { get; set; } = string.Empty;
  }

  public sealed class GameplayScreenStateMachine
  {
    private string lastStateHash = string.Empty;

    public GameplayScreenStateViewModel Evaluate(
      GameplayScreenState requested,
      ClientViewMode mode,
      string currentStateHash,
      bool backendAvailable = true,
      bool replayMismatch = false)
    {
      var state = requested;
      if (!backendAvailable && requested == GameplayScreenState.Booting)
      {
        state = GameplayScreenState.BackendUnavailable;
      }
      else if (mode == ClientViewMode.Spectator && requested == GameplayScreenState.MatchActive)
      {
        state = GameplayScreenState.Spectating;
      }
      else if (mode == ClientViewMode.DebugBoth && requested == GameplayScreenState.MatchActive)
      {
        state = GameplayScreenState.DebugProjectionSwitch;
      }
      else if (replayMismatch)
      {
        state = GameplayScreenState.Faulted;
      }

      var hashChanged = !string.IsNullOrWhiteSpace(currentStateHash) &&
        !string.Equals(lastStateHash, currentStateHash, StringComparison.Ordinal);
      if (!string.IsNullOrWhiteSpace(currentStateHash))
      {
        lastStateHash = currentStateHash;
      }

      return new GameplayScreenStateViewModel
      {
        State = state,
        StatusText = ToStatusText(state),
        Blocking = IsBlocking(state),
        Recoverable = IsRecoverable(state),
        CommandsEnabled = AllowsCommands(state),
        ResetsTargetSelection = hashChanged,
        StateHash = currentStateHash ?? string.Empty
      };
    }

    public GameplayScreenStateViewModel OnBootSucceeded() =>
      Evaluate(GameplayScreenState.ReadyToSeed, ClientViewMode.PlayerOne, lastStateHash);

    public GameplayScreenStateViewModel OnSeedSucceeded() =>
      Evaluate(GameplayScreenState.ReadyToStart, ClientViewMode.PlayerOne, lastStateHash);

    public GameplayScreenStateViewModel OnStartSucceeded(ClientViewMode mode, string stateHash) =>
      Evaluate(GameplayScreenState.MatchActive, mode, stateHash);

    public GameplayScreenStateViewModel OnStaleHash() =>
      Evaluate(GameplayScreenState.Reconnecting, ClientViewMode.PlayerOne, lastStateHash);

    public GameplayScreenStateViewModel OnReplayMismatch() =>
      Evaluate(GameplayScreenState.Faulted, ClientViewMode.PlayerOne, lastStateHash, replayMismatch: true);

    public GameplayScreenStateViewModel OnTargetCancelled(ClientViewMode mode, string stateHash) =>
      Evaluate(GameplayScreenState.MatchActive, mode, stateHash);

    private static bool AllowsCommands(GameplayScreenState state) =>
      state is GameplayScreenState.MatchActive or GameplayScreenState.DebugProjectionSwitch;

    private static bool IsBlocking(GameplayScreenState state) =>
      state is GameplayScreenState.Faulted or GameplayScreenState.Terminal;

    private static bool IsRecoverable(GameplayScreenState state) =>
      state is GameplayScreenState.BackendUnavailable or GameplayScreenState.Reconnecting;

    private static string ToStatusText(GameplayScreenState state) =>
      state switch
      {
        GameplayScreenState.Booting => "Booting backend capabilities.",
        GameplayScreenState.BackendUnavailable => "Backend unavailable; retry boot after starting the local API.",
        GameplayScreenState.ReadyToSeed => "Ready to seed demo data.",
        GameplayScreenState.ReadyToStart => "Ready to start local match.",
        GameplayScreenState.StartingMatch => "Starting local match.",
        GameplayScreenState.MatchActive => "Match active.",
        GameplayScreenState.SelectingTarget => "Select a highlighted backend-provided target.",
        GameplayScreenState.ResolvingChoice => "Resolving visible choice.",
        GameplayScreenState.SubmittingCommand => "Submitting server-provided command.",
        GameplayScreenState.Reconnecting => "Reconnecting and replacing projection from backend.",
        GameplayScreenState.ReplayVerifying => "Verifying canonical replay.",
        GameplayScreenState.Terminal => "Match is terminal.",
        GameplayScreenState.Faulted => "Blocking fault.",
        GameplayScreenState.Spectating => "Spectator projection; commands disabled.",
        GameplayScreenState.DebugProjectionSwitch => "DebugBoth projection; development-only.",
        _ => "Unknown gameplay screen state."
      };
  }
}

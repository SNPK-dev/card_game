using System;
using CardGame.Unity.Client.DebugTools;

namespace CardGame.Unity.Client.Match
{
  public enum ClientViewMode
  {
    PlayerOne,
    PlayerTwo,
    Spectator,
    DebugBoth
  }

  public sealed class ClientModeState
  {
    public const string DebugBothWarning =
      "DebugBoth is a development-only projection switcher. It does not merge hidden state.";

    private string playerOneId = DebugDemoSeeder.PlayerOneId;
    private string playerTwoId = DebugDemoSeeder.PlayerTwoId;
    private string spectatorId = DebugDemoSeeder.SpectatorId;
    public ClientViewMode Mode { get; private set; } = ClientViewMode.PlayerOne;
    public string ActivePlayerId { get; private set; } = DebugDemoSeeder.PlayerOneId;
    public bool CommandsEnabled => Mode == ClientViewMode.PlayerOne || Mode == ClientViewMode.PlayerTwo;
    public bool IsSpectator => Mode == ClientViewMode.Spectator;
    public bool IsDebugBoth => Mode == ClientViewMode.DebugBoth;

    public string ProjectionKey => Mode switch
    {
      ClientViewMode.PlayerOne => playerOneId,
      ClientViewMode.PlayerTwo => playerTwoId,
      ClientViewMode.Spectator => spectatorId,
      ClientViewMode.DebugBoth => ActivePlayerId,
      _ => playerOneId
    };

    public void ConfigurePlayers(string firstPlayerId, string secondPlayerId, string matchSpectatorId = DebugDemoSeeder.SpectatorId)
    {
      playerOneId = string.IsNullOrWhiteSpace(firstPlayerId) ? DebugDemoSeeder.PlayerOneId : firstPlayerId;
      playerTwoId = string.IsNullOrWhiteSpace(secondPlayerId) ? DebugDemoSeeder.PlayerTwoId : secondPlayerId;
      spectatorId = string.IsNullOrWhiteSpace(matchSpectatorId) ? DebugDemoSeeder.SpectatorId : matchSpectatorId;
      SetMode(ClientViewMode.PlayerOne);
    }

    public void SetMode(ClientViewMode mode)
    {
      Mode = mode;
      ActivePlayerId = mode switch
      {
        ClientViewMode.PlayerTwo => playerTwoId,
        ClientViewMode.Spectator => spectatorId,
        _ => playerOneId
      };
    }

    public void CycleMode()
    {
      var next = Mode switch
      {
        ClientViewMode.PlayerOne => ClientViewMode.PlayerTwo,
        ClientViewMode.PlayerTwo => ClientViewMode.Spectator,
        ClientViewMode.Spectator => ClientViewMode.DebugBoth,
        _ => ClientViewMode.PlayerOne
      };
      SetMode(next);
    }

    public void SwitchDebugProjection()
    {
      if (Mode != ClientViewMode.DebugBoth)
      {
        CycleMode();
        return;
      }

      ActivePlayerId = string.Equals(ActivePlayerId, playerTwoId, StringComparison.OrdinalIgnoreCase) ? playerOneId : playerTwoId;
    }
  }

  public enum ClientUiState
  {
    Booting,
    ReadyToSeed,
    ReadyToStart,
    MatchActive,
    SelectingTarget,
    ResolvingChoice,
    SubmittingCommand,
    Reconnecting,
    Terminal,
    Faulted
  }
}

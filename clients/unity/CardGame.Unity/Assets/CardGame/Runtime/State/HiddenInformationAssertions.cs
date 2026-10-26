using System;
using System.Collections.Generic;
using System.Linq;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.State
{
  public static class HiddenInformationAssertions
  {
    public static IReadOnlyList<HiddenInformationAssertionResult> CheckAll(
      MatchStateView playerOne,
      MatchStateView playerTwo,
      MatchStateView spectator,
      string playerOneId = "player-1",
      string playerTwoId = "player-2")
    {
      var results = new List<HiddenInformationAssertionResult>
      {
        CheckPlayerProjection(playerOne, playerOneId),
        CheckPlayerProjection(playerTwo, playerTwoId),
        CheckSpectatorProjection(spectator)
      };
      return results;
    }

    public static HiddenInformationAssertionResult CheckPlayerProjection(MatchStateView state, string viewerPlayerId)
    {
      if (state == null)
      {
        return Fail("player-projection-" + viewerPlayerId, "Projection was null.");
      }

      if (!string.Equals(state.ViewKind, "PLAYER", StringComparison.OrdinalIgnoreCase))
      {
        return Fail("player-projection-" + viewerPlayerId, "Projection was not a PLAYER view.");
      }

      if (!string.Equals(state.ViewerPlayerId, viewerPlayerId, StringComparison.OrdinalIgnoreCase))
      {
        return Fail("player-projection-" + viewerPlayerId, "Viewer player id did not match requested player.");
      }

      foreach (var player in state.Players.Values)
      {
        if (string.Equals(player.PlayerId, viewerPlayerId, StringComparison.OrdinalIgnoreCase))
        {
          var ownDeckLeak = FirstIdentityLeak(player.Zones?.Deck, allowVisible: false);
          if (ownDeckLeak != null)
          {
            return Fail("player-projection-" + viewerPlayerId, "Own deck exposed identity: " + ownDeckLeak);
          }

          continue;
        }

        var leak = FirstHiddenZoneLeak(player);
        if (leak != null)
        {
          return Fail("player-projection-" + viewerPlayerId, "Opponent hidden zone exposed identity: " + leak);
        }
      }

      return Pass("player-projection-" + viewerPlayerId, "Opponent hidden identities are absent.");
    }

    public static HiddenInformationAssertionResult CheckSpectatorProjection(MatchStateView state)
    {
      if (state == null)
      {
        return Fail("spectator-projection", "Projection was null.");
      }

      if (!string.Equals(state.ViewKind, "SPECTATOR", StringComparison.OrdinalIgnoreCase))
      {
        return Fail("spectator-projection", "Projection was not a SPECTATOR view.");
      }

      foreach (var player in state.Players.Values)
      {
        var leak = FirstHiddenZoneLeak(player);
        if (leak != null)
        {
          return Fail("spectator-projection", "Spectator hidden zone exposed identity: " + leak);
        }
      }

      return Pass("spectator-projection", "Spectator hidden identities are absent.");
    }

    public static HiddenInformationAssertionResult CheckProjectionSwitch(MatchStateView current, string expectedViewerPlayerId)
    {
      if (current == null)
      {
        return Fail("projection-switch", "Current projection was null after switch.");
      }

      if (!string.Equals(current.ViewerPlayerId, expectedViewerPlayerId, StringComparison.OrdinalIgnoreCase))
      {
        return Fail("projection-switch", "Store did not replace the current viewer projection.");
      }

      var playerCheck = CheckPlayerProjection(current, expectedViewerPlayerId);
      return playerCheck.Passed
        ? Pass("projection-switch", "Projection replacement did not merge hidden identities.")
        : Fail("projection-switch", playerCheck.Message);
    }

    private static string FirstHiddenZoneLeak(PublicPlayerStateView player)
    {
      if (player?.Zones == null)
      {
        return null;
      }

      return FirstIdentityLeak(player.Zones.Hand, allowVisible: true) ??
        FirstIdentityLeak(player.Zones.Deck, allowVisible: false) ??
        FirstIdentityLeak(player.Zones.SecretZone, allowVisible: true) ??
        FirstIdentityLeak(player.Zones.SetAside, allowVisible: true);
    }

    private static string FirstIdentityLeak(PublicZoneView zone, bool allowVisible)
    {
      if (zone?.Cards == null)
      {
        return null;
      }

      foreach (var card in zone.Cards)
      {
        var visible = allowVisible && string.Equals(card.Visibility, "VISIBLE", StringComparison.OrdinalIgnoreCase);
        if (visible)
        {
          continue;
        }

        if (!string.IsNullOrWhiteSpace(card.CardId) ||
          !string.IsNullOrWhiteSpace(card.CardVersion) ||
          !string.IsNullOrWhiteSpace(card.CardInstanceId))
        {
          return zone.Zone + ":" + (card.CardId ?? card.CardVersion ?? card.CardInstanceId);
        }
      }

      return null;
    }

    private static HiddenInformationAssertionResult Pass(string name, string message) =>
      new() { Name = name, Passed = true, Message = message };

    private static HiddenInformationAssertionResult Fail(string name, string message) =>
      new() { Name = name, Passed = false, Message = message };
  }

  public sealed class HiddenInformationAssertionResult
  {
    public string Name { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Message { get; set; } = string.Empty;
  }
}

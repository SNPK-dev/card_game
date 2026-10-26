using Game.Application.Capabilities;
using Game.Application.Catalog;
using Game.Application.Decks;
using Game.Application.Matches;
using Game.Application.Matchmaking;
using Game.Application.Players;
using Game.Application.Rulesets;
using Game.Domain.State;
using Game.Infrastructure.Storage;
using Game.Shared.Catalog;
using Game.Shared.Decks;
using Game.Shared.Dsl;
using Game.Shared.Events;
using Game.Shared.Primitives;
using Game.Shared.Ruleset;
using Xunit;

namespace Game.Integration.Tests;

public sealed class ExternalMatchApiFlowTests
{
  private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-07-01T00:00:00Z");

  [Fact]
  public void PublicMatchApiSupportsLegalActionsSubmissionPollingReconnectOwnershipAndReplay()
  {
    var service = CreateService(out var catalogStore, out var deckStore, out var profileStore);
    var catalog = CreateCatalog();
    catalogStore.Store(catalog);
    profileStore.Store(CreateProfile());
    deckStore.Store(CreateDeck("deck-p1", "player-1", catalog.SnapshotId));
    deckStore.Store(CreateDeck("deck-p2", "player-2", catalog.SnapshotId));

    var player1 = service.CreateSession(new CreateApiSessionRequest("player-1", "session-p1"));
    var player2 = service.CreateSession(new CreateApiSessionRequest("player-2", "session-p2"));
    var spectator = service.CreateSession(new CreateApiSessionRequest("spectator-1", "session-s1"));
    Assert.True(player1.Succeeded);
    Assert.True(player2.Succeeded);
    Assert.True(spectator.Succeeded);

    var start = service.Start(new ExternalMatchStartRequest(
      "match-public-api",
      "game-public-api",
      ["deck-p1", "deck-p2"],
      new Dictionary<string, string>(StringComparer.Ordinal)
      {
        ["player-1"] = "session-p1",
        ["player-2"] = "session-p2"
      },
      "player-1",
      ["session-s1"]));
    Assert.True(start.Succeeded, string.Join("; ", start.Diagnostics.Select(diagnostic => $"{diagnostic.Code}:{diagnostic.Path}:{diagnostic.Identifier}")));
    Assert.Equal("player-1", start.State?.ActivePlayerId);

    var legal = service.GetLegalActions("match-public-api", "session-p1", "player-1");
    Assert.True(legal.Succeeded);
    var endTurn = Assert.Single(legal.LegalActions, command => command.CommandType == "END_TURN");

    var forbidden = service.GetLegalActions("match-public-api", "session-p2", "player-1");
    Assert.False(forbidden.Succeeded);
    Assert.Contains(forbidden.Diagnostics, diagnostic => diagnostic.Code == "MATCH_COMMAND_FORBIDDEN");

    var spectatorState = service.GetState("match-public-api", "session-s1");
    Assert.NotNull(spectatorState);
    Assert.Equal(start.State?.StateHash, spectatorState.StateHash);

    var spectatorLegal = service.GetLegalActions("match-public-api", "session-s1", "player-1");
    Assert.False(spectatorLegal.Succeeded);
    Assert.Contains(spectatorLegal.Diagnostics, diagnostic => diagnostic.Code == "MATCH_COMMAND_FORBIDDEN");

    var staleHash = service.SubmitCommand(
      "match-public-api",
      new ExternalCommandSubmissionRequest(
        "session-p1",
        endTurn,
        "command-public-api-stale",
        "stale-hash"));
    Assert.False(staleHash.Succeeded);
    Assert.Contains(staleHash.Diagnostics, diagnostic => diagnostic.Code == "COMMAND_PRE_STATE_HASH_MISMATCH");

    var submission = service.SubmitCommand(
      "match-public-api",
      new ExternalCommandSubmissionRequest(
        "session-p1",
        endTurn,
        "command-public-api-1",
        start.State!.StateHash));
    Assert.True(submission.Succeeded);
    Assert.NotEmpty(submission.Events);
    Assert.Equal("player-2", submission.State?.ActivePlayerId);

    var spectatorCommand = service.SubmitCommand(
      "match-public-api",
      new ExternalCommandSubmissionRequest(
        "session-s1",
        endTurn,
        "command-public-api-spectator",
        start.State!.StateHash));
    Assert.False(spectatorCommand.Succeeded);
    Assert.Contains(spectatorCommand.Diagnostics, diagnostic => diagnostic.Code == "MATCH_COMMAND_FORBIDDEN");

    var events = service.PollEvents("match-public-api", "session-p1", afterEventSeq: 0);
    Assert.True(events.Succeeded);
    Assert.Equal(submission.Events.Count, events.Events.Count);
    Assert.Equal(submission.State?.NextEventSeq, events.NextEventSeq);

    var spectatorEvents = service.PollEvents("match-public-api", "session-s1", afterEventSeq: 0);
    Assert.True(spectatorEvents.Succeeded);
    Assert.Equal(events.Events.Count, spectatorEvents.Events.Count);

    var reconnect = service.Reconnect("match-public-api", "session-p1", afterEventSeq: 0);
    Assert.True(reconnect.Succeeded);
    Assert.Equal(submission.State?.StateHash, reconnect.State?.StateHash);
    Assert.Equal(events.Events.Count, reconnect.Events.Count);

    var replay = service.VerifyReplay("match-public-api", "session-p1");
    Assert.True(replay.Succeeded);
    Assert.True(replay.Replay.IsVerified);

    var spectatorReplay = service.VerifyReplay("match-public-api", "session-s1");
    Assert.True(spectatorReplay.Succeeded);
    Assert.True(spectatorReplay.Replay.IsVerified);
  }

  [Fact]
  public void StarterHeroPowerExecutesFromPinnedRuntimeDefinitionAndReplays()
  {
    var service = CreateService(out var catalogStore, out var deckStore, out var profileStore);
    var catalog = CreateCatalog();
    catalogStore.Store(catalog);
    profileStore.Store(CreateProfile() with
    {
      EnabledMechanics = ["HERO_POWER_ACTIVATION"],
      AllowedActions = ["PLAY_CARD", "END_TURN", "CONCEDE", "USE_HERO_POWER", "HEAL"],
      AllowedCardTypes = ["MINION_CARD", "HERO_POWER_CARD"],
      AllowedEntityTypes = ["MINION", "HERO"],
      AllowedClasses = ["neutral", StarterHeroProfiles.AstralWardenId]
    });
    deckStore.Store(CreateDeck("deck-power-p1", "player-1", catalog.SnapshotId, StarterHeroProfiles.AstralWardenId));
    deckStore.Store(CreateDeck("deck-power-p2", "player-2", catalog.SnapshotId, StarterHeroProfiles.CinderVanguardId));
    Assert.True(service.CreateSession(new CreateApiSessionRequest("player-1", "power-session-p1")).Succeeded);
    Assert.True(service.CreateSession(new CreateApiSessionRequest("player-2", "power-session-p2")).Succeeded);

    var start = service.Start(new ExternalMatchStartRequest(
      "match-starter-power",
      "game-starter-power",
      ["deck-power-p1", "deck-power-p2"],
      new Dictionary<string, string>(StringComparer.Ordinal)
      {
        ["player-1"] = "power-session-p1",
        ["player-2"] = "power-session-p2"
      },
      "player-1",
      []));
    Assert.True(start.Succeeded, string.Join("; ", start.Diagnostics.Select(diagnostic => diagnostic.Code)));

    var p1End = Assert.Single(
      service.GetLegalActions("match-starter-power", "power-session-p1", "player-1").LegalActions,
      command => command.CommandType == "END_TURN");
    var afterP1 = service.SubmitCommand(
      "match-starter-power",
      new ExternalCommandSubmissionRequest("power-session-p1", p1End, "power-end-p1", start.State!.StateHash));
    Assert.True(afterP1.Succeeded);
    var p2End = Assert.Single(
      service.GetLegalActions("match-starter-power", "power-session-p2", "player-2").LegalActions,
      command => command.CommandType == "END_TURN");
    var afterP2 = service.SubmitCommand(
      "match-starter-power",
      new ExternalCommandSubmissionRequest("power-session-p2", p2End, "power-end-p2", afterP1.State!.StateHash));
    Assert.True(afterP2.Succeeded);
    Assert.Equal(1, afterP2.State!.Players["player-1"].Hero!.Damage);

    var power = Assert.Single(
      service.GetLegalActions("match-starter-power", "power-session-p1", "player-1").LegalActions,
      command => command.CommandType == "USE_HERO_POWER"
        && command.Refs is not null
        && command.Refs.TryGetValue("targetEntityInstanceId", out var target)
        && target == "hero:player-1");
    var used = service.SubmitCommand(
      "match-starter-power",
      new ExternalCommandSubmissionRequest("power-session-p1", power, "power-use-astra", afterP2.State.StateHash));

    Assert.True(used.Succeeded, string.Join("; ", used.Diagnostics.Select(diagnostic => diagnostic.Code)));
    Assert.Equal(0, used.State!.Players["player-1"].Mana);
    Assert.Equal(0, used.State.Players["player-1"].Hero!.Damage);
    Assert.Contains(used.Events, @event => @event.EventType == "HERO_POWER_USED");
    Assert.Contains(used.Events, @event => @event.EventType == "ENTITY_HEALED");

    var p1EndsAgain = Assert.Single(
      service.GetLegalActions("match-starter-power", "power-session-p1", "player-1").LegalActions,
      command => command.CommandType == "END_TURN");
    var vargTurn = service.SubmitCommand(
      "match-starter-power",
      new ExternalCommandSubmissionRequest("power-session-p1", p1EndsAgain, "power-end-p1-again", used.State.StateHash));
    Assert.True(vargTurn.Succeeded);
    var vargPower = Assert.Single(
      service.GetLegalActions("match-starter-power", "power-session-p2", "player-2").LegalActions,
      command => command.CommandType == "USE_HERO_POWER"
        && command.Refs is not null
        && command.Refs.TryGetValue("targetEntityInstanceId", out var target)
        && target == "hero:player-1");
    var vargUsed = service.SubmitCommand(
      "match-starter-power",
      new ExternalCommandSubmissionRequest("power-session-p2", vargPower, "power-use-varg", vargTurn.State!.StateHash));
    Assert.True(vargUsed.Succeeded, string.Join("; ", vargUsed.Diagnostics.Select(diagnostic => diagnostic.Code)));
    Assert.Equal(0, vargUsed.State!.Players["player-2"].Mana);
    Assert.Equal(1, vargUsed.State.Players["player-1"].Hero!.Damage);
    Assert.Contains(vargUsed.Events, @event => @event.EventType == "ENTITY_DAMAGED");
    Assert.True(service.VerifyReplay("match-starter-power", "power-session-p1").Replay.IsVerified);
  }

  [Fact]
  public void MatchStateViewsRedactOpponentAndSpectatorHiddenInformation()
  {
    var service = CreateService(out var catalogStore, out var deckStore, out var profileStore, out _, out var runtimeStore);
    var catalog = CreateCatalog();
    catalogStore.Store(catalog);
    profileStore.Store(CreateProfile());
    deckStore.Store(CreateDeck("deck-p1", "player-1", catalog.SnapshotId));
    deckStore.Store(CreateDeck("deck-p2", "player-2", catalog.SnapshotId));

    Assert.True(service.CreateSession(new CreateApiSessionRequest("player-1", "session-p1")).Succeeded);
    Assert.True(service.CreateSession(new CreateApiSessionRequest("player-2", "session-p2")).Succeeded);
    Assert.True(service.CreateSession(new CreateApiSessionRequest("spectator-1", "session-s1")).Succeeded);
    var start = service.Start(new ExternalMatchStartRequest(
      "match-hidden-info-api",
      "game-hidden-info-api",
      ["deck-p1", "deck-p2"],
      new Dictionary<string, string>(StringComparer.Ordinal)
      {
        ["player-1"] = "session-p1",
        ["player-2"] = "session-p2"
      },
      "player-1",
      ["session-s1"]));
    Assert.True(start.Succeeded, string.Join("; ", start.Diagnostics.Select(diagnostic => diagnostic.Code)));

    var runtimeRecord = runtimeStore.Find("match-hidden-info-api")!;
    var runtimeState = (InMemoryGameState)runtimeRecord.State;
    var opponentState = runtimeState.Players["player-2"];
    var graveyardCard = opponentState.Zones.Hand.Single();
    runtimeState = runtimeState.WithPlayer(opponentState with
    {
      Zones = opponentState.Zones.MoveCard(graveyardCard.CardInstanceId, SourceZone.Hand, SourceZone.Graveyard)
    });
    runtimeStore.Update(runtimeRecord with { State = runtimeState });

    var player1View = service.GetState("match-hidden-info-api", "session-p1");
    var player2View = service.GetState("match-hidden-info-api", "session-p2");
    var spectatorView = service.GetState("match-hidden-info-api", "session-s1");

    Assert.NotNull(player1View);
    Assert.NotNull(player2View);
    Assert.NotNull(spectatorView);
    Assert.Equal("PLAYER", player1View.ViewKind);
    Assert.Equal("player-1", player1View.ViewerPlayerId);
    Assert.Equal("SPECTATOR", spectatorView.ViewKind);
    Assert.Null(spectatorView.ViewerPlayerId);

    var player1Opponent = player1View.Players["player-2"];
    Assert.Equal("OPPONENT", player1Opponent.Relationship);
    Assert.Equal(0, player1Opponent.Zones.Deck.Count);
    Assert.Empty(player1Opponent.Zones.Deck.Cards);
    Assert.Equal(0, player1Opponent.Zones.Hand.Count);
    Assert.Empty(player1Opponent.Zones.Hand.Cards);
    Assert.Equal(1, player1Opponent.Zones.Graveyard.Count);
    Assert.Empty(player1Opponent.Zones.Graveyard.Cards);
    Assert.All(player1Opponent.Secrets ?? [], secret =>
    {
      if (!secret.IsRevealed)
      {
        Assert.Null(secret.SourceCardInstanceId);
        Assert.Null(secret.SourceCardVersion);
        Assert.Equal("HIDDEN", secret.Visibility);
      }
    });

    var player2Opponent = player2View.Players["player-1"];
    Assert.Equal("OPPONENT", player2Opponent.Relationship);
    Assert.Equal(0, player2Opponent.Zones.Deck.Count);
    Assert.Empty(player2Opponent.Zones.Deck.Cards);
    Assert.Equal(1, player2Opponent.Zones.Hand.Count);
    Assert.Empty(player2Opponent.Zones.Hand.Cards);
    Assert.Equal(1, player2View.Players["player-2"].Zones.Graveyard.Count);
    Assert.Single(player2View.Players["player-2"].Zones.Graveyard.Cards);

    Assert.All(spectatorView.Players.Values, player =>
    {
      Assert.Equal("SPECTATED", player.Relationship);
      Assert.Empty(player.Zones.Deck.Cards);
      Assert.Empty(player.Zones.Hand.Cards);
      Assert.Empty(player.Zones.Graveyard.Cards);
      Assert.Empty(player.Zones.SecretZone.Cards);
    });
  }

  [Fact]
  public void PublicViewsFilterPrivateChoicesOwnerOnlyEventsAndDebugOnlyEvents()
  {
    var service = CreateService(out var catalogStore, out var deckStore, out var profileStore, out _, out var runtimeStore);
    var catalog = CreateCatalog();
    catalogStore.Store(catalog);
    profileStore.Store(CreateProfile());
    deckStore.Store(CreateDeck("deck-p1", "player-1", catalog.SnapshotId));
    deckStore.Store(CreateDeck("deck-p2", "player-2", catalog.SnapshotId));

    Assert.True(service.CreateSession(new CreateApiSessionRequest("player-1", "session-p1")).Succeeded);
    Assert.True(service.CreateSession(new CreateApiSessionRequest("player-2", "session-p2")).Succeeded);
    Assert.True(service.CreateSession(new CreateApiSessionRequest("spectator-1", "session-s1")).Succeeded);
    var start = service.Start(new ExternalMatchStartRequest(
      "match-private-visibility-api",
      "game-private-visibility-api",
      ["deck-p1", "deck-p2"],
      new Dictionary<string, string>(StringComparer.Ordinal)
      {
        ["player-1"] = "session-p1",
        ["player-2"] = "session-p2"
      },
      "player-1",
      ["session-s1"]));
    Assert.True(start.Succeeded, string.Join("; ", start.Diagnostics.Select(diagnostic => diagnostic.Code)));

    var record = runtimeStore.Find("match-private-visibility-api")!;
    var state = (InMemoryGameState)record.State;
    var stateWithChoices = state
      .WithChoiceCreated(CreateChoice("choice-private-p1", "player-1", "PRIVATE"))
      .WithChoiceCreated(CreateChoice("choice-private-p2", "player-2", "PRIVATE"))
      .WithChoiceCreated(CreateChoice("choice-public", "player-2", "PUBLIC"));
    runtimeStore.Update(record with
    {
      State = stateWithChoices,
      EventLog =
      [
        CreateEvent(1, CommittedEventType.CardPlayed, EventVisibility.Public, "player-1"),
        CreateEvent(2, CommittedEventType.ChoiceCreated, EventVisibility.OwnerOnly, "player-1"),
        CreateEvent(3, CommittedEventType.ChoiceCreated, EventVisibility.OwnerOnly, "player-2"),
        CreateEvent(4, CommittedEventType.RngRolled, EventVisibility.DebugOnly, "player-1")
      ],
      NextEventSeq = 5
    });

    var player1State = service.GetState("match-private-visibility-api", "session-p1")!;
    var player2State = service.GetState("match-private-visibility-api", "session-p2")!;
    var spectatorState = service.GetState("match-private-visibility-api", "session-s1")!;

    Assert.Contains("choice-private-p1", player1State.PendingChoices.Keys);
    Assert.DoesNotContain("choice-private-p2", player1State.PendingChoices.Keys);
    Assert.Contains("choice-public", player1State.PendingChoices.Keys);
    Assert.Contains("choice-private-p2", player2State.PendingChoices.Keys);
    Assert.DoesNotContain("choice-private-p1", player2State.PendingChoices.Keys);
    Assert.Contains("choice-public", player2State.PendingChoices.Keys);
    Assert.Equal(["choice-public"], spectatorState.PendingChoices.Keys.Order(StringComparer.Ordinal));

    var player1Events = service.PollEvents("match-private-visibility-api", "session-p1", afterEventSeq: 0);
    var player2Events = service.PollEvents("match-private-visibility-api", "session-p2", afterEventSeq: 0);
    var spectatorEvents = service.PollEvents("match-private-visibility-api", "session-s1", afterEventSeq: 0);

    Assert.Equal(["CARD_PLAYED", "CHOICE_CREATED"], player1Events.Events.Select(@event => @event.EventType));
    Assert.Equal([1, 2], player1Events.Events.Select(@event => @event.EventSeq));
    Assert.Equal([1, 3], player2Events.Events.Select(@event => @event.EventSeq));
    Assert.Equal([1], spectatorEvents.Events.Select(@event => @event.EventSeq));
    Assert.DoesNotContain(player1Events.Events.Concat(player2Events.Events).Concat(spectatorEvents.Events), @event => @event.EventType == "RNG_ROLLED");

    var reconnect = service.Reconnect("match-private-visibility-api", "session-s1", afterEventSeq: 0);
    Assert.True(reconnect.Succeeded);
    Assert.Equal(["choice-public"], reconnect.State!.PendingChoices.Keys.Order(StringComparer.Ordinal));
    Assert.Equal([1], reconnect.Events.Select(@event => @event.EventSeq));
  }

  [Fact]
  public void PublicMatchRuntimeSurvivesStoreReloadAndVerifiesReplay()
  {
    var root = Path.Combine(Path.GetTempPath(), $"card-game-match-runtime-{Guid.NewGuid():N}");
    try
    {
      var catalogStore = new JsonFileCatalogSnapshotStore(root);
      var deckStore = new JsonFileDeckStore(root);
      var profileStore = new JsonFileResolvedRulesetProfileStore(root);
      var sessionStore = new JsonFileApiSessionStore(root);
      var runtimeStore = new JsonFileMatchRuntimeStore(root);
      var service = CreateService(catalogStore, deckStore, profileStore, sessionStore, runtimeStore);
      var catalog = CreateCatalog();
      catalogStore.Store(catalog);
      profileStore.Store(CreateProfile());
      deckStore.Store(CreateDeck("deck-p1", "player-1", catalog.SnapshotId));
      deckStore.Store(CreateDeck("deck-p2", "player-2", catalog.SnapshotId));

      Assert.True(service.CreateSession(new CreateApiSessionRequest("player-1", "session-p1")).Succeeded);
      Assert.True(service.CreateSession(new CreateApiSessionRequest("player-2", "session-p2")).Succeeded);
      Assert.True(service.CreateSession(new CreateApiSessionRequest("spectator-1", "session-s1")).Succeeded);
      var start = service.Start(new ExternalMatchStartRequest(
        "match-reload-api",
        "game-reload-api",
        ["deck-p1", "deck-p2"],
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
          ["player-1"] = "session-p1",
          ["player-2"] = "session-p2"
        },
        "player-1",
        ["session-s1"]));
      Assert.True(start.Succeeded, string.Join("; ", start.Diagnostics.Select(diagnostic => $"{diagnostic.Code}:{diagnostic.Path}:{diagnostic.Identifier}")));

      var endTurn = Assert.Single(
        service.GetLegalActions("match-reload-api", "session-p1", "player-1").LegalActions,
        command => command.CommandType == "END_TURN");
      var submission = service.SubmitCommand(
        "match-reload-api",
        new ExternalCommandSubmissionRequest(
          "session-p1",
          endTurn,
          "command-reload-api-1",
          start.State!.StateHash));
      Assert.True(submission.Succeeded);

      var reloadedService = CreateService(
        new JsonFileCatalogSnapshotStore(root),
        new JsonFileDeckStore(root),
        new JsonFileResolvedRulesetProfileStore(root),
        new JsonFileApiSessionStore(root),
        new JsonFileMatchRuntimeStore(root));

      var state = reloadedService.GetState("match-reload-api", "session-p1");
      Assert.NotNull(state);
      Assert.Equal(submission.State!.StateHash, state.StateHash);
      Assert.Equal(submission.State.NextEventSeq, state.NextEventSeq);
      Assert.Equal(submission.State.CommandCount, state.CommandCount);

      var events = reloadedService.PollEvents("match-reload-api", "session-p2", afterEventSeq: 0);
      Assert.True(events.Succeeded);
      Assert.Equal(submission.Events.Count, events.Events.Count);
      Assert.Equal(submission.State.NextEventSeq, events.NextEventSeq);

      var spectatorState = reloadedService.GetState("match-reload-api", "session-s1");
      Assert.NotNull(spectatorState);
      Assert.Equal(submission.State.StateHash, spectatorState.StateHash);

      var spectatorEvents = reloadedService.PollEvents("match-reload-api", "session-s1", afterEventSeq: 0);
      Assert.True(spectatorEvents.Succeeded);
      Assert.Equal(events.Events.Count, spectatorEvents.Events.Count);

      var forbidden = reloadedService.SubmitCommand(
        "match-reload-api",
        new ExternalCommandSubmissionRequest("session-p2", endTurn, "command-reload-forbidden"));
      Assert.False(forbidden.Succeeded);
      Assert.Contains(forbidden.Diagnostics, diagnostic => diagnostic.Code == "MATCH_COMMAND_FORBIDDEN");

      var spectatorCommand = reloadedService.SubmitCommand(
        "match-reload-api",
        new ExternalCommandSubmissionRequest("session-s1", endTurn, "command-reload-spectator-forbidden"));
      Assert.False(spectatorCommand.Succeeded);
      Assert.Contains(spectatorCommand.Diagnostics, diagnostic => diagnostic.Code == "MATCH_COMMAND_FORBIDDEN");

      var replay = reloadedService.VerifyReplay("match-reload-api", "session-p1");
      Assert.True(replay.Succeeded);
      Assert.True(replay.Replay.IsVerified);

      var spectatorReplay = reloadedService.VerifyReplay("match-reload-api", "session-s1");
      Assert.True(spectatorReplay.Succeeded);
      Assert.True(spectatorReplay.Replay.IsVerified);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public void MatchStartRejectsInvalidSpectatorSessions()
  {
    var service = CreateService(out var catalogStore, out var deckStore, out var profileStore);
    var catalog = CreateCatalog();
    catalogStore.Store(catalog);
    profileStore.Store(CreateProfile());
    deckStore.Store(CreateDeck("deck-p1", "player-1", catalog.SnapshotId));
    deckStore.Store(CreateDeck("deck-p2", "player-2", catalog.SnapshotId));

    Assert.True(service.CreateSession(new CreateApiSessionRequest("player-1", "session-p1")).Succeeded);
    Assert.True(service.CreateSession(new CreateApiSessionRequest("player-2", "session-p2")).Succeeded);

    var playerTokenAsSpectator = service.Start(new ExternalMatchStartRequest(
      "match-invalid-spectator-player",
      "game-invalid-spectator",
      ["deck-p1", "deck-p2"],
      new Dictionary<string, string>(StringComparer.Ordinal)
      {
        ["player-1"] = "session-p1",
        ["player-2"] = "session-p2"
      },
      "player-1",
      ["session-p1"]));
    Assert.False(playerTokenAsSpectator.Succeeded);
    Assert.Contains(playerTokenAsSpectator.Diagnostics, diagnostic => diagnostic.Code == "MATCH_SPECTATOR_SESSION_INVALID");

    var unknownSpectator = service.Start(new ExternalMatchStartRequest(
      "match-invalid-spectator-unknown",
      "game-invalid-spectator",
      ["deck-p1", "deck-p2"],
      new Dictionary<string, string>(StringComparer.Ordinal)
      {
        ["player-1"] = "session-p1",
        ["player-2"] = "session-p2"
      },
      "player-1",
      ["session-missing"]));
    Assert.False(unknownSpectator.Succeeded);
    Assert.Contains(unknownSpectator.Diagnostics, diagnostic => diagnostic.Code == "MATCH_SPECTATOR_SESSION_INVALID");
  }

  [Fact]
  public void PublicMatchApiRejectsExpiredSessionsAtStartAndRuntimeBoundaries()
  {
    var timeProvider = new AdjustableTimeProvider(FixedNow);
    var service = CreateService(
      timeProvider,
      out var catalogStore,
      out var deckStore,
      out var profileStore);
    var catalog = CreateCatalog();
    catalogStore.Store(catalog);
    profileStore.Store(CreateProfile());
    deckStore.Store(CreateDeck("deck-p1", "player-1", catalog.SnapshotId));
    deckStore.Store(CreateDeck("deck-p2", "player-2", catalog.SnapshotId));

    Assert.True(service.CreateSession(new CreateApiSessionRequest("player-1", "expired-p1", Ttl: TimeSpan.FromMinutes(1))).Succeeded);
    Assert.True(service.CreateSession(new CreateApiSessionRequest("player-2", "session-p2", Ttl: TimeSpan.FromHours(1))).Succeeded);
    timeProvider.Advance(TimeSpan.FromMinutes(2));

    var expiredStart = service.Start(new ExternalMatchStartRequest(
      "match-expired-start",
      "game-expired-start",
      ["deck-p1", "deck-p2"],
      new Dictionary<string, string>(StringComparer.Ordinal)
      {
        ["player-1"] = "expired-p1",
        ["player-2"] = "session-p2"
      },
      "player-1"));
    Assert.False(expiredStart.Succeeded);
    Assert.Contains(expiredStart.Diagnostics, diagnostic => diagnostic.Code == "MATCH_PLAYER_SESSION_REQUIRED");

    timeProvider.SetUtcNow(FixedNow);
    Assert.True(service.CreateSession(new CreateApiSessionRequest("player-1", "session-p1", Ttl: TimeSpan.FromMinutes(1))).Succeeded);
    Assert.True(service.CreateSession(new CreateApiSessionRequest("spectator-1", "session-s1", Ttl: TimeSpan.FromMinutes(1))).Succeeded);
    var start = service.Start(new ExternalMatchStartRequest(
      "match-expired-runtime",
      "game-expired-runtime",
      ["deck-p1", "deck-p2"],
      new Dictionary<string, string>(StringComparer.Ordinal)
      {
        ["player-1"] = "session-p1",
        ["player-2"] = "session-p2"
      },
      "player-1",
      ["session-s1"]));
    Assert.True(start.Succeeded, string.Join("; ", start.Diagnostics.Select(diagnostic => diagnostic.Code)));

    var legal = service.GetLegalActions("match-expired-runtime", "session-p1", "player-1");
    Assert.True(legal.Succeeded);
    var endTurn = Assert.Single(legal.LegalActions, command => command.CommandType == "END_TURN");
    timeProvider.Advance(TimeSpan.FromMinutes(2));

    var expiredState = service.GetState("match-expired-runtime", "session-p1");
    Assert.Null(expiredState);

    var expiredSpectatorEvents = service.PollEvents("match-expired-runtime", "session-s1", afterEventSeq: 0);
    Assert.False(expiredSpectatorEvents.Succeeded);
    Assert.Contains(expiredSpectatorEvents.Diagnostics, diagnostic => diagnostic.Code == "MATCH_SESSION_FORBIDDEN");

    var expiredCommand = service.SubmitCommand(
      "match-expired-runtime",
      new ExternalCommandSubmissionRequest("session-p1", endTurn, "command-expired-session", start.State!.StateHash));
    Assert.False(expiredCommand.Succeeded);
    Assert.Contains(expiredCommand.Diagnostics, diagnostic => diagnostic.Code == "MATCH_SESSION_FORBIDDEN");

    var expiredReplay = service.VerifyReplay("match-expired-runtime", "session-s1");
    Assert.False(expiredReplay.Succeeded);
    Assert.Contains(expiredReplay.Diagnostics, diagnostic => diagnostic.Code == "MATCH_SESSION_FORBIDDEN");
  }

  private static MatchRuntimeService CreateService(
    out InMemoryCatalogSnapshotStore catalogStore,
    out InMemoryDeckStore deckStore,
    out InMemoryResolvedRulesetProfileStore profileStore) =>
    CreateService(
      TimeProvider.System,
      out catalogStore,
      out deckStore,
      out profileStore);

  private static MatchRuntimeService CreateService(
    TimeProvider timeProvider,
    out InMemoryCatalogSnapshotStore catalogStore,
    out InMemoryDeckStore deckStore,
    out InMemoryResolvedRulesetProfileStore profileStore)
  {
    catalogStore = new InMemoryCatalogSnapshotStore();
    deckStore = new InMemoryDeckStore();
    profileStore = new InMemoryResolvedRulesetProfileStore();
    return CreateService(
      catalogStore,
      deckStore,
      profileStore,
      new InMemoryApiSessionStore(),
      new InMemoryMatchRuntimeStore(),
      timeProvider);
  }

  private static MatchRuntimeService CreateService(
    out InMemoryCatalogSnapshotStore catalogStore,
    out InMemoryDeckStore deckStore,
    out InMemoryResolvedRulesetProfileStore profileStore,
    out InMemoryApiSessionStore sessionStore,
    out InMemoryMatchRuntimeStore runtimeStore)
  {
    catalogStore = new InMemoryCatalogSnapshotStore();
    deckStore = new InMemoryDeckStore();
    profileStore = new InMemoryResolvedRulesetProfileStore();
    sessionStore = new InMemoryApiSessionStore();
    runtimeStore = new InMemoryMatchRuntimeStore();
    return CreateService(
      catalogStore,
      deckStore,
      profileStore,
      sessionStore,
      runtimeStore,
      TimeProvider.System);
  }

  private static MatchRuntimeService CreateService(
    ICatalogSnapshotStore catalogStore,
    IDeckStore deckStore,
    IResolvedRulesetProfileStore profileStore,
    IApiSessionStore sessionStore,
    IMatchRuntimeStore runtimeStore,
    TimeProvider? timeProvider = null)
  {
    var catalogQuery = new CatalogSnapshotQueryService(catalogStore);
    var deckQuery = new DeckQueryService(deckStore);
    var profileQuery = new ResolvedRulesetProfileQueryService(profileStore);
    var capabilityProvider = new DefaultEngineCapabilityRegistryProvider();
    var readiness = new MatchReadinessService(
      deckQuery,
      catalogQuery,
      profileQuery,
      capabilityProvider,
      new DeckValidator(new CatalogCompatibilityValidator(new EngineCapabilityCompatibilityValidator())),
      new EngineCapabilityCompatibilityValidator(),
      new Game.Application.Rulesets.DefaultRulesetDefinitionRegistry());
    var start = new MatchStartService(deckQuery, catalogQuery, readiness);

    return new MatchRuntimeService(
      runtimeStore,
      sessionStore,
      start,
      deckQuery,
      catalogQuery,
      profileQuery,
      capabilityProvider,
      timeProvider ?? TimeProvider.System);
  }

  private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
  {
    private DateTimeOffset utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => utcNow;

    public void Advance(TimeSpan value) => utcNow = utcNow.Add(value);

    public void SetUtcNow(DateTimeOffset value) => utcNow = value;
  }

  private static CatalogSnapshot CreateCatalog()
  {
    var card = CreateCard();
    return new CatalogSnapshot(
      "catalog-public-api",
      "ruleset_mvp_core_v1",
      "sha256:public-api-profile",
      FixedNow,
      [card]);
  }

  private static CardVersion CreateCard() =>
    new(
      "card_public_minion",
      "card_public_minion_v1",
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      "catalog-public-api",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:public-api-profile",
      FixedNow,
      new CardDefinition(
        "card_public_minion",
        "card_public_minion_v1",
        "card-dsl-0.2.0",
        "engine-bootstrap-0.3.0",
        "primitive-registry-bootstrap-0.2.0",
        "ruleset_mvp_core_v1",
        "catalog-public-api",
        new StaticCardData(
          "Public Minion",
          CardType.MinionCard,
          ["neutral"],
          Rarity.Common,
          1,
          [],
          [],
          Collectible: true,
          ["mvp"]),
        new EntityDefinition(
          new MinionDefinition(1, 1, [], [], CanAttack: true),
          Spell: null,
          Weapon: null,
          Location: null,
          HeroCard: null,
          HeroPower: null),
        Effects: [],
        new CardTextGeneration("A public API minion.", FlavorText: null),
        new BalanceMetadata(1, 0, []),
        new AssetMetadata(null, null, null),
        new AuthoringMetadata("test", FixedNow, "pipeline-v1", [])));

  private static DeckDefinition CreateDeck(
    string deckId,
    string playerId,
    string catalogId,
    string starterHeroId = StarterHeroProfiles.AstralWardenId)
  {
    var hero=StarterHeroProfiles.Find(starterHeroId)!;
    return new DeckDefinition(
      deckId,
      playerId,
      catalogId,
      "ruleset_mvp_core_v1",
      [new DeckCardRef("card_public_minion", 1)],
      hero.HeroId,
      hero.DisplayName,
      hero.PortraitAssetRef,
      hero.StartingHealth);
  }

  private static ResolvedRulesetProfile CreateProfile() =>
    new(
      "resolved-public-api",
      "ruleset_mvp_core_v1",
      "0.2.0",
      FixedNow,
      "test",
      "sha256:public-api-profile",
      "mvp",
      null,
      [],
      [],
      [],
      ["SIMPLE_PLAY_EFFECT"],
      ["PLAY_CARD", "END_TURN", "CONCEDE"],
      [],
      ["HAND", "DECK", "BOARD", "GRAVEYARD", "SET_ASIDE"],
      ["ON_PLAY"],
      [],
      ["MINION_CARD"],
      ["MINION"],
      ["COMMON"],
      ["neutral"],
      [],
      new RulesetLimits(30, 25, 20, 80, 7, 1, "LIMITED", 3, 2, 3, 3, 3, 2, 2, 3, 1, 7, 10),
      new ValidationPolicyRef("validation", true, true, true, true, true, true, true, true, false),
      new BalancePolicyRef("balance", false, "MEDIUM", [], 0.05, 30, 25, 0.1, 0, 0, "baseline", "seed", "none"),
      new SimulationPolicyRef("simulation", [], [], 0, 0, false, 30, 80),
      new PublicationPolicyRef("publication", true, false, false, false, false, false, false, ["INTERNAL"]),
      new LegacyPolicy(false, false, false, false, true));

  private static PendingChoice CreateChoice(string choiceId, string playerId, string visibilityPolicy) =>
    new(
      choiceId,
      playerId,
      "DISCOVER",
      visibilityPolicy,
      "HAND",
      "test-source",
      [
        new ChoiceOption($"{choiceId}-option-1", "CARD", $"{choiceId}-card-1"),
        new ChoiceOption($"{choiceId}-option-2", "CARD", $"{choiceId}-card-2")
      ]);

  private static CommittedEventRecord CreateEvent(
    long eventSeq,
    CommittedEventType eventType,
    EventVisibility visibility,
    string controllerPlayerId) =>
    new(
      eventSeq,
      null,
      eventSeq,
      eventType,
      EventCategory.Committed,
      Phase.OnPlay,
      [new EventRef("PLAYER", controllerPlayerId, null, controllerPlayerId)],
      [],
      [],
      $"batch:{eventSeq:0000}",
      [$"mutation:{eventSeq:0000}"],
      [],
      $"sha256:{eventSeq:D64}",
      $"sha256:{eventSeq + 1:D64}",
      visibility);
}

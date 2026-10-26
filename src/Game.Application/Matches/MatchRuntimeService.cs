using Game.Application.Capabilities;
using Game.Application.Catalog;
using Game.Application.Commands;
using Game.Application.Decks;
using Game.Application.Execution;
using Game.Application.Legality;
using Game.Application.Matchmaking;
using Game.Application.Players;
using Game.Application.Replay;
using Game.Application.Rulesets;
using Game.Domain.Commands;
using Game.Domain.Events;
using Game.Domain.Hashing;
using Game.Domain.Mutation;
using Game.Domain.Replay;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Shared.Primitives;

namespace Game.Application.Matches;

public sealed class MatchRuntimeService(
  IMatchRuntimeStore store,
  IApiSessionStore sessionStore,
  IMatchStartService matchStartService,
  DeckQueryService deckQuery,
  CatalogSnapshotQueryService catalogQuery,
  ResolvedRulesetProfileQueryService profileQuery,
  IEngineCapabilityRegistryProvider capabilityProvider,
  TimeProvider timeProvider)
{
  private readonly StateHasher stateHasher = new(new CanonicalJsonSerializer());
  private readonly LegalActionOracle legalActionOracle = new();
  private readonly CommandAdmissionService admissionService = new();

  public CreateApiSessionResult CreateSession(CreateApiSessionRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    if (string.IsNullOrWhiteSpace(request.PlayerId))
    {
      return CreateApiSessionResult.Rejected(
      [
        Diagnostic("SESSION_PLAYER_ID_REQUIRED", "playerId", string.Empty, "Player id is required.")
      ]);
    }

    var now = timeProvider.GetUtcNow();
    var sessionRequest = request.Now is null
      ? request with { Now = now }
      : request;
    return CreateApiSessionResult.Success(sessionStore.Store(sessionRequest));
  }

  public ExternalMatchStartResult Start(ExternalMatchStartRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    var diagnostics = new List<MatchRuntimeDiagnostic>();
    if (string.IsNullOrWhiteSpace(request.MatchId))
    {
      diagnostics.Add(Diagnostic("MATCH_ID_REQUIRED", "matchId", string.Empty, "Match id is required."));
    }

    var decks = request.DeckIds
      .Select(deckQuery.GetDeck)
      .Where(deck => deck is not null)
      .Select(deck => deck!)
      .ToArray();
    foreach (var deck in decks)
    {
      if (!request.PlayerSessionTokens.TryGetValue(deck.PlayerId, out var sessionToken)
        || sessionStore.FindByToken(sessionToken, timeProvider.GetUtcNow())?.PlayerId != deck.PlayerId)
      {
        diagnostics.Add(Diagnostic(
          "MATCH_PLAYER_SESSION_REQUIRED",
          $"playerSessionTokens.{deck.PlayerId}",
          deck.PlayerId,
          "Starting a public match requires a valid session token for each deck owner."));
      }
    }

    var spectatorTokens = (request.SpectatorSessionTokens ?? [])
      .Where(token => !string.IsNullOrWhiteSpace(token))
      .Distinct(StringComparer.Ordinal)
      .Order(StringComparer.Ordinal)
      .ToArray();
    foreach (var spectatorToken in spectatorTokens)
    {
      var session = sessionStore.FindByToken(spectatorToken, timeProvider.GetUtcNow());
      if (session is null || request.PlayerSessionTokens.ContainsKey(session.PlayerId))
      {
        diagnostics.Add(Diagnostic(
          "MATCH_SPECTATOR_SESSION_INVALID",
          "spectatorSessionTokens",
          string.Empty,
          "Spectator sessions must be valid non-player sessions."));
      }
    }

    if (diagnostics.Count > 0)
    {
      return ExternalMatchStartResult.Rejected(diagnostics);
    }

    var start = matchStartService.Start(new MatchStartRequest(
      request.GameId,
      request.DeckIds,
      request.StartingPlayerId));
    if (!start.Succeeded || start.State is null)
    {
      return ExternalMatchStartResult.Rejected(start.Diagnostics.Select(ToRuntimeDiagnostic).ToArray());
    }

    var deckCatalogs = decks
      .Select(deck => catalogQuery.GetSnapshot(deck.CatalogSnapshotId))
      .Where(catalog => catalog is not null)
      .Select(catalog => catalog!)
      .ToArray();
    var cardVersions = deckCatalogs
      .SelectMany(catalog => catalog.Cards.Concat(StarterHeroPowerCatalog.CreateAll(catalog)))
      .GroupBy(card => card.CardVersionId, StringComparer.Ordinal)
      .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    var firstCatalog = deckCatalogs.FirstOrDefault();
    var profile = firstCatalog is null
      ? null
      : profileQuery.GetProfileBySourceAndHash(firstCatalog.RulesetProfileId, firstCatalog.RulesetProfileHash);
    var record = new MatchRuntimeRecord(
      request.MatchId,
      start.State,
      start.State,
      request.PlayerSessionTokens,
      spectatorTokens,
      cardVersions,
      profile,
      [],
      [],
      1);

    if (!store.TryAdd(record))
    {
      return ExternalMatchStartResult.Rejected(
      [
        Diagnostic("MATCH_ALREADY_EXISTS", "matchId", request.MatchId, "A match with this id already exists.")
      ]);
    }

    return ExternalMatchStartResult.Success(record.MatchId, ToStateView(record, null, "SPECTATOR"));
  }

  public MatchStateView? GetState(string matchId, string sessionToken) =>
    IsAuthorizedReader(matchId, sessionToken, out var record, out var session, out _)
      ? ToStateView(record!, ViewerPlayerId(record!, session!), ViewKind(record!, session!))
      : null;

  public ExternalLegalActionsResult GetLegalActions(string matchId, string sessionToken, string playerId)
  {
    if (!IsAuthorizedPlayer(matchId, sessionToken, playerId, out var record, out _, out var diagnostic))
    {
      return ExternalLegalActionsResult.Rejected([diagnostic]);
    }

    var result = legalActionOracle.GetLegalActions(CreateLegalQuery(record!, playerId));
    return ExternalLegalActionsResult.Success(
      result.Commands.Select(ToDto).ToArray(),
      result.Diagnostics);
  }

  public ExternalCommandSubmissionResult SubmitCommand(string matchId, ExternalCommandSubmissionRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    if (!IsAuthorizedPlayer(matchId, request.SessionToken, request.Command.PlayerId, out var record, out var session, out var diagnostic))
    {
      return ExternalCommandSubmissionResult.Rejected([diagnostic]);
    }

    var currentStateHash = stateHasher.HashGameplayState(record!.State);
    if (!string.IsNullOrWhiteSpace(request.ExpectedPreStateHash)
      && !StringComparer.Ordinal.Equals(request.ExpectedPreStateHash, currentStateHash))
    {
      return ExternalCommandSubmissionResult.Rejected(
      [
        Diagnostic(
          "COMMAND_PRE_STATE_HASH_MISMATCH",
          "expectedPreStateHash",
          request.ExpectedPreStateHash,
          "Submitted command expected pre-state hash does not match the current match state.")
      ]);
    }

    var commandSeq = record!.CommandLog.Count + 1;
    var envelope = new GameCommandEnvelope(
      string.IsNullOrWhiteSpace(request.CommandId) ? $"command:{matchId}:{commandSeq:D4}" : request.CommandId,
      timeProvider.GetUtcNow(),
      null,
      request.CorrelationId,
      request.SessionToken,
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["matchId"] = matchId
      },
      null,
      commandSeq,
      request.Command.PlayerId,
      request.Command.CommandType,
      request.Command.Payload,
      request.Command.Refs,
      request.ExpectedPreStateHash,
      CommandCategory.Player);
    var admission = admissionService.Admit(envelope, CreateLegalQuery(record, request.Command.PlayerId));
    if (!admission.IsAccepted || admission.Payload is null)
    {
      return ExternalCommandSubmissionResult.Rejected(admission.Errors
        .Select(error => Diagnostic(error.Code.ToString(), "command", error.CommandSeq.ToString(), error.Message))
        .ToArray());
    }

    var command = admission.Payload.ToCommand();
    var execution = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      record.CardVersionsByVersionId.Values.ToArray())
      .Execute(record.State, command, record.NextEventSeq);
    var updated = record with
    {
      State = execution.State,
      CommandLog = record.CommandLog.Append(admission.Payload).ToArray(),
      EventLog = record.EventLog.Concat(execution.Events).ToArray(),
      NextEventSeq = record.NextEventSeq + execution.Events.Count
    };
    store.Update(updated);

    return ExternalCommandSubmissionResult.Success(
      ToStateView(updated, ViewerPlayerId(updated, session!), ViewKind(updated, session!)),
      FilterEvents(updated, session!, execution.Events));
  }

  public MatchEventsPollResult PollEvents(string matchId, string sessionToken, long afterEventSeq)
  {
    if (!IsAuthorizedReader(matchId, sessionToken, out var record, out var session, out var diagnostic))
    {
      return MatchEventsPollResult.Rejected([diagnostic]);
    }

    var events = record!.EventLog
      .Where(@event => @event.EventSeq > afterEventSeq)
      .OrderBy(@event => @event.EventSeq)
      .ToArray();
    return MatchEventsPollResult.Success(FilterEvents(record, session!, events), record.NextEventSeq);
  }

  public MatchReconnectResult Reconnect(string matchId, string sessionToken, long afterEventSeq)
  {
    if (!IsAuthorizedReader(matchId, sessionToken, out var record, out var session, out var diagnostic))
    {
      return MatchReconnectResult.Rejected([diagnostic]);
    }

    var events = record!.EventLog
      .Where(@event => @event.EventSeq > afterEventSeq)
      .OrderBy(@event => @event.EventSeq)
      .ToArray();
    return MatchReconnectResult.Success(
      ToStateView(record, ViewerPlayerId(record, session!), ViewKind(record, session!)),
      FilterEvents(record, session!, events),
      record.NextEventSeq);
  }

  public MatchReplayVerificationView VerifyReplay(string matchId, string sessionToken)
  {
    if (!IsAuthorizedReader(matchId, sessionToken, out var record, out _, out var diagnostic))
    {
      return MatchReplayVerificationView.Rejected([diagnostic]);
    }

    var replay = new ReplayVerificationService(
      new CommandLogReplayer(new StateMutator(), new DerivedEventLog(), stateHasher, record!.CardVersionsByVersionId.Values.ToArray()),
      stateHasher)
      .Verify(record.InitialState, new CanonicalCommandLog(record.CommandLog), stateHasher.HashGameplayState(record.State));
    return MatchReplayVerificationView.Success(replay);
  }

  private LegalActionQuery CreateLegalQuery(MatchRuntimeRecord record, string playerId) =>
    new(
      record.State,
      playerId,
      record.CardVersionsByVersionId,
      record.RulesetProfile,
      capabilityProvider.GetCurrentRegistry());

  private bool IsAuthorizedReader(
    string matchId,
    string sessionToken,
    out MatchRuntimeRecord? record,
    out ApiSession? session,
    out MatchRuntimeDiagnostic diagnostic)
  {
    session = null;
    record = store.Find(matchId);
    if (record is null)
    {
      diagnostic = Diagnostic("MATCH_NOT_FOUND", "matchId", matchId, "Match was not found.");
      return false;
    }

    session = sessionStore.FindByToken(sessionToken, timeProvider.GetUtcNow());
    if (session is null
      || (!record.PlayerSessionTokens.ContainsKey(session.PlayerId)
        && !record.SpectatorSessionTokens.Contains(session.SessionToken, StringComparer.Ordinal)))
    {
      diagnostic = Diagnostic("MATCH_SESSION_FORBIDDEN", "sessionToken", string.Empty, "Session is not authorized for this match.");
      return false;
    }

    diagnostic = null!;
    return true;
  }

  private bool IsAuthorizedPlayer(
    string matchId,
    string sessionToken,
    string playerId,
    out MatchRuntimeRecord? record,
    out ApiSession? session,
    out MatchRuntimeDiagnostic diagnostic)
  {
    if (!IsAuthorizedReader(matchId, sessionToken, out record, out session, out diagnostic))
    {
      return false;
    }

    if (session is null || !StringComparer.Ordinal.Equals(session.PlayerId, playerId))
    {
      diagnostic = Diagnostic("MATCH_COMMAND_FORBIDDEN", "playerId", playerId, "Session cannot act for another player.");
      return false;
    }

    return true;
  }

  private MatchStateView ToStateView(MatchRuntimeRecord record, string? viewerPlayerId, string viewKind) =>
    new(
      record.MatchId,
      viewKind,
      viewerPlayerId,
      record.State.Revision,
      stateHasher.HashGameplayState(record.State),
      record.State.ActivePlayerId,
      record.State.TurnNumber,
      record.State.Phase,
      record.State.TerminalState.IsTerminal,
      record.State.TerminalState.Reason,
      record.State.Players
        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .ToDictionary(pair => pair.Key, pair => ToPlayerView(record, pair.Value, viewerPlayerId), StringComparer.Ordinal),
      record.State.PendingChoices
        .Where(pair => ChoiceVisibleTo(pair.Value, viewerPlayerId, viewKind))
        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .ToDictionary(pair => pair.Key, pair => ToChoiceView(pair.Value, viewerPlayerId), StringComparer.Ordinal),
      record.NextEventSeq,
      record.CommandLog.Count);

  private static string? ViewerPlayerId(MatchRuntimeRecord record, ApiSession session) =>
    record.PlayerSessionTokens.ContainsKey(session.PlayerId) ? session.PlayerId : null;

  private static string ViewKind(MatchRuntimeRecord record, ApiSession session) =>
    record.PlayerSessionTokens.ContainsKey(session.PlayerId) ? "PLAYER" : "SPECTATOR";

  private static IReadOnlyList<PublicMatchEventRecord> FilterEvents(
    MatchRuntimeRecord record,
    ApiSession session,
    IReadOnlyList<Game.Shared.Events.CommittedEventRecord> events)
  {
    var viewerPlayerId = ViewerPlayerId(record, session);
    return events
      .Where(@event => EventVisibleTo(@event, viewerPlayerId))
      .Select(ToPublicEvent)
      .ToArray();
  }

  private static PublicMatchEventRecord ToPublicEvent(Game.Shared.Events.CommittedEventRecord @event) =>
    new(
      @event.EventSeq,
      @event.ParentEventSeq,
      @event.CommandSeq,
      ToJsonEnumName(@event.EventType),
      ToJsonEnumName(@event.Category),
      ToJsonEnumName(@event.Phase),
      @event.SourceRefs.Select(ToPublicEventRef).ToArray(),
      @event.TargetRefs.Select(ToPublicEventRef).ToArray(),
      @event.Payload.Select(ToPublicEventPayloadEntry).ToArray(),
      @event.MutationBatchRef,
      @event.MutationRefs,
      @event.RngRollRefs,
      @event.PreStateHash,
      @event.PostStateHash,
      ToJsonEnumName(@event.Visibility));

  private static PublicEventRef ToPublicEventRef(Game.Shared.Events.EventRef reference) =>
    new(reference.RefType, reference.RefId, reference.Zone, reference.ControllerPlayerId);

  private static PublicEventPayloadEntry ToPublicEventPayloadEntry(Game.Shared.Events.EventPayloadEntry entry) =>
    new(
      entry.Key,
      new PublicEventPayloadValue(
        entry.Value.ValueKind,
        entry.Value.StringValue,
        entry.Value.IntValue,
        entry.Value.BoolValue,
        entry.Value.StringValues));

  private static string ToJsonEnumName<TEnum>(TEnum value)
    where TEnum : struct, Enum
  {
    var name = value.ToString();
    return string.Concat(name.Select((character, index) =>
      index > 0 && char.IsUpper(character)
        ? "_" + character
        : character.ToString())).ToUpperInvariant();
  }

  private static bool EventVisibleTo(Game.Shared.Events.CommittedEventRecord @event, string? viewerPlayerId)
  {
    if (@event.Visibility is Game.Shared.Events.EventVisibility.Public)
    {
      return true;
    }

    if (string.IsNullOrWhiteSpace(viewerPlayerId))
    {
      return false;
    }

    if (@event.Visibility is Game.Shared.Events.EventVisibility.DebugOnly)
    {
      return false;
    }

    var relatedPlayerIds = @event.SourceRefs
      .Concat(@event.TargetRefs)
      .Select(reference => reference.ControllerPlayerId)
      .Where(playerId => !string.IsNullOrWhiteSpace(playerId));

    return relatedPlayerIds.Contains(viewerPlayerId, StringComparer.Ordinal);
  }

  private static PublicPlayerStateView ToPlayerView(MatchRuntimeRecord record, PlayerState player, string? viewerPlayerId)
  {
    var relationship = viewerPlayerId is null
      ? "SPECTATED"
      : StringComparer.Ordinal.Equals(player.PlayerId, viewerPlayerId)
        ? "SELF"
        : "OPPONENT";
    var canSeePrivate = relationship is "SELF";

    return new PublicPlayerStateView(
      player.PlayerId,
      relationship,
      new PublicPlayerZonesView(
        Deck: ToZoneView(record, player, "DECK", player.Zones.Deck, canSeePrivate ? "PRIVATE" : "HIDDEN", revealCards: false),
        Hand: ToZoneView(record, player, "HAND", player.Zones.Hand, canSeePrivate ? "PRIVATE" : "HIDDEN", revealCards: canSeePrivate),
        Graveyard: ToZoneView(record, player, "GRAVEYARD", player.Zones.Graveyard, canSeePrivate ? "PRIVATE" : "HIDDEN", revealCards: canSeePrivate),
        SecretZone: ToZoneView(record, player, "SECRET_ZONE", player.Zones.SecretZone, canSeePrivate ? "PRIVATE" : "HIDDEN", revealCards: canSeePrivate),
        SetAside: ToZoneView(record, player, "SET_ASIDE", player.Zones.SetAside, canSeePrivate ? "PRIVATE" : "HIDDEN", revealCards: canSeePrivate),
        Board: player.Zones.Board.Select(entity => ToEntityView(entity)!).ToArray()),
      ToEntityView(player.Hero),
      player.Mana,
      player.MaxMana,
      player.Armor,
      player.FatigueCounter,
      player.Modifiers.Select(ToModifierView).ToArray(),
      ToEntityView(player.Weapon),
      ToEntityView(player.Location),
      player.HeroPower is null ? null : new PublicHeroPowerView(
        player.HeroPower.SourceCardInstanceId,
        player.HeroPower.Cost,
        player.HeroPower.ActivationTiming),
      player.Quests?.Select(ToQuestView).ToArray() ?? [],
      player.Secrets?.Select(secret => ToSecretView(secret, canSeePrivate)).ToArray() ?? [],
      player.Auras?.Select(ToAuraView).ToArray() ?? []);
  }

  private static PublicZoneView ToZoneView(
    MatchRuntimeRecord record,
    PlayerState player,
    string zone,
    IReadOnlyList<CardInstance> cards,
    string visibility,
    bool revealCards) =>
    new(
      zone,
      cards.Count,
      revealCards ? cards.Select(card => ToCardView(record, player, card, visibility)).ToArray() : []);

  private static PublicCardInstanceView ToCardView(MatchRuntimeRecord record, PlayerState player, CardInstance card, string visibility)
  {
    record.CardVersionsByVersionId.TryGetValue(card.CardVersion, out var version);
    var baseManaCost = version?.Definition.StaticData.ManaCost;
    var reduction = baseManaCost.HasValue
      ? CostModifierCalculator.CalculateReduction(player, baseManaCost.Value, record.RulesetProfile?.Limits.MaxManaCostReduction)
      : 0;
    return new(
      card.CardInstanceId,
      card.CardId,
      card.CardVersion,
      card.OwnerPlayerId,
      ToCardTypeId(card.CardType),
      visibility,
      baseManaCost,
      baseManaCost.HasValue ? Math.Max(0, baseManaCost.Value - reduction) : null);
  }

  private static PublicEntityView? ToEntityView(EntityInstance? entity) =>
    entity is null
      ? null
      : new PublicEntityView(
        entity.EntityInstanceId,
        entity.SourceCardInstanceId,
        entity.ControllerPlayerId,
        entity.EntityType.ToString(),
        entity.Attack,
        entity.Health,
        entity.Damage,
        entity.Keywords);

  private static PublicPlayerModifierView ToModifierView(PlayerModifier modifier) =>
    new(
      modifier.ModifierId,
      modifier.ModifierType,
      modifier.Amount,
      modifier.Duration,
      modifier.Priority,
      modifier.SourceRef);

  private static PublicQuestView ToQuestView(PlayerQuest quest) =>
    new(
      quest.QuestId,
      quest.SourceCardInstanceId,
      quest.ControllerPlayerId,
      quest.ProgressEventType,
      quest.Progress,
      quest.RequiredProgress,
      quest.IsCompleted,
      quest.RewardEffectId,
      quest.SourceCardVersion);

  private static PublicSecretView ToSecretView(PlayerSecret secret, bool canSeePrivate)
  {
    var reveal = canSeePrivate || secret.IsRevealed;
    return new PublicSecretView(
      secret.SecretId,
      secret.ControllerPlayerId,
      secret.IsRevealed,
      reveal ? "PUBLIC" : "HIDDEN",
      reveal ? secret.SourceCardInstanceId : null,
      reveal ? secret.SourceCardVersion : null,
      reveal ? secret.RevealEventType : null,
      reveal ? secret.EffectId : null);
  }

  private static PublicAuraView ToAuraView(PlayerAura aura) =>
    new(
      aura.AuraId,
      aura.SourceEntityInstanceId,
      aura.SourceCardInstanceId,
      aura.SourceCardVersion,
      aura.ControllerPlayerId,
      aura.EffectId,
      aura.Priority);

  private static bool ChoiceVisibleTo(PendingChoice choice, string? viewerPlayerId, string viewKind)
  {
    if (StringComparer.Ordinal.Equals(choice.VisibilityPolicy, "PUBLIC"))
    {
      return true;
    }

    return viewKind is "PLAYER" && StringComparer.Ordinal.Equals(choice.PlayerId, viewerPlayerId);
  }

  private static PublicChoiceView ToChoiceView(PendingChoice choice, string? viewerPlayerId)
  {
    var canSeeOptions = StringComparer.Ordinal.Equals(choice.VisibilityPolicy, "PUBLIC")
      || StringComparer.Ordinal.Equals(choice.PlayerId, viewerPlayerId);
    return new PublicChoiceView(
      choice.ChoiceId,
      choice.PlayerId,
      choice.ChoiceType,
      choice.VisibilityPolicy,
      choice.ResultZone,
      choice.SourceRef,
      choice.Options
        .Select(option => new PublicChoiceOptionView(
          option.OptionId,
          option.ItemKind,
          canSeeOptions ? option.ItemId : null,
          canSeeOptions ? "PUBLIC" : "HIDDEN"))
        .ToArray());
  }

  private static MatchCommandDto ToDto(IGameCommand command) =>
    command switch
    {
      PlayCardCommand play => new MatchCommandDto(
        play.Kind,
        play.PlayerId,
        play.Parameters,
        CreatePlayCardRefs(play)),
      AttackCommand attack => new MatchCommandDto(
        attack.Kind,
        attack.PlayerId,
        attack.Parameters,
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["attackerEntityInstanceId"] = attack.AttackerEntityInstanceId,
          ["targetPlayerId"] = attack.TargetPlayerId,
          ["targetEntityInstanceId"] = attack.TargetEntityInstanceId
        }),
      EndTurnCommand endTurn => new MatchCommandDto(endTurn.Kind, endTurn.PlayerId, endTurn.Parameters),
      ConcedeCommand concede => new MatchCommandDto(concede.Kind, concede.PlayerId, concede.Parameters),
      ActivateLocationCommand activate => new MatchCommandDto(
        activate.Kind,
        activate.PlayerId,
        activate.Parameters,
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["locationEntityInstanceId"] = activate.LocationEntityInstanceId,
          ["sourceCardInstanceId"] = activate.SourceCardInstanceId,
          ["targetPlayerId"] = activate.TargetPlayerId,
          ["targetEntityInstanceId"] = activate.TargetEntityInstanceId
        }),
      UseHeroPowerCommand heroPower => new MatchCommandDto(
        heroPower.Kind,
        heroPower.PlayerId,
        heroPower.Parameters,
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["sourceCardInstanceId"] = heroPower.SourceCardInstanceId,
          ["targetPlayerId"] = heroPower.TargetPlayerId,
          ["targetEntityInstanceId"] = heroPower.TargetEntityInstanceId
        }),
      ResolveChoiceCommand choice => new MatchCommandDto(
        choice.Kind,
        choice.PlayerId,
        choice.Parameters,
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["choiceId"] = choice.ChoiceId,
          ["selectedOptionId"] = choice.SelectedOptionId,
          ["selectedOptionIds"] = string.Join(",", choice.SelectedOptionIds),
          ["cardInstanceId"] = choice.CardInstanceId,
          ["cardVersion"] = choice.CardVersion,
          ["cardType"] = ToCardTypeId(choice.CardType)
        }),
      _ => new MatchCommandDto(command.Kind, string.Empty, command.Parameters)
    };

  private static IReadOnlyDictionary<string, string> CreatePlayCardRefs(PlayCardCommand play)
  {
    var refs = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
      ["cardInstanceId"] = play.CardInstanceId,
      ["entityInstanceId"] = play.EntityInstanceId,
      ["heroPowerActivationTiming"] = play.HeroPowerActivationTiming
    };
    if(!string.IsNullOrWhiteSpace(play.TargetPlayerId))refs["targetPlayerId"]=play.TargetPlayerId;
    if(!string.IsNullOrWhiteSpace(play.TargetEntityInstanceId))refs["targetEntityInstanceId"]=play.TargetEntityInstanceId;
    var keywords = play.Keywords ?? [];
    for (var index = 0; index < keywords.Count; index++)
    {
      refs[$"entityKeyword:{index + 1:0000}"] = keywords[index];
    }

    return refs;
  }

  private static string ToCardTypeId(CardType cardType) =>
    cardType switch
    {
      CardType.MinionCard => "MINION_CARD",
      CardType.SpellCard => "SPELL_CARD",
      CardType.WeaponCard => "WEAPON_CARD",
      CardType.LocationCard => "LOCATION_CARD",
      CardType.HeroCard => "HERO_CARD",
      CardType.HeroPowerCard => "HERO_POWER_CARD",
      CardType.TokenCard => "TOKEN_CARD",
      _ => cardType.ToString()
    };

  private static MatchRuntimeDiagnostic ToRuntimeDiagnostic(MatchStartDiagnostic diagnostic) =>
    Diagnostic(diagnostic.Code, diagnostic.Path, diagnostic.Identifier, diagnostic.Message);

  private static MatchRuntimeDiagnostic Diagnostic(string code, string path, string identifier, string message) =>
    new(code, path, identifier, message);
}

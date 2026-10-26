using Game.Domain.Hashing;
using Game.Domain.Events;
using Game.Domain.Mutation;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Shared.Events;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Domain.Tests;

public sealed class CoreStateModelTests
{
  private readonly IStateHasher _hasher = new StateHasher(new CanonicalJsonSerializer());

  [Fact]
  public void CardInstanceCannotExistInMultipleCardZones()
  {
    var card = CreateCard("card-instance-1");

    Assert.Throws<InvalidOperationException>(() =>
      new PlayerZones(deck: [card], hand: [card]));
  }

  [Fact]
  public void PlayerZonesMoveCardRemovesItFromPreviousZone()
  {
    var card = CreateCard("card-instance-1");
    var zones = new PlayerZones(deck: [card]);

    var moved = zones.MoveCard(card.CardInstanceId, SourceZone.Deck, SourceZone.Hand);

    Assert.Empty(moved.Deck);
    Assert.Single(moved.Hand);
    Assert.Equal(card.CardInstanceId, moved.Hand[0].CardInstanceId);
  }

  [Fact]
  public void PlayerZonesArePartOfGameplayStateHash()
  {
    var card = CreateCard("card-instance-1");
    var deckState = CreateState(new PlayerZones(deck: [card]));
    var handState = CreateState(new PlayerZones(hand: [card]));

    Assert.NotEqual(
      _hasher.HashGameplayState(deckState),
      _hasher.HashGameplayState(handState));
  }

  [Fact]
  public void TurnMetadataIsPartOfGameplayStateHash()
  {
    var first = CreateState(PlayerZones.Empty, activePlayerId: "player-1", turnNumber: 1, phase: "TURN_START");
    var second = CreateState(PlayerZones.Empty, activePlayerId: "player-2", turnNumber: 1, phase: "TURN_START");
    var third = CreateState(PlayerZones.Empty, activePlayerId: "player-1", turnNumber: 2, phase: "TURN_START");
    var fourth = CreateState(PlayerZones.Empty, activePlayerId: "player-1", turnNumber: 1, phase: "TURN_END");

    Assert.NotEqual(_hasher.HashGameplayState(first), _hasher.HashGameplayState(second));
    Assert.NotEqual(_hasher.HashGameplayState(first), _hasher.HashGameplayState(third));
    Assert.NotEqual(_hasher.HashGameplayState(first), _hasher.HashGameplayState(fourth));
  }

  [Fact]
  public void StateMutatorMovesCardBetweenZonesAndRecordsPostStateHash()
  {
    var card = CreateCard("card-instance-1");
    var state = CreateState(new PlayerZones(deck: [card]));
    var batch = CreateMoveCardBatch(state, card.CardInstanceId, SourceZone.Deck, SourceZone.Hand);

    var application = new StateMutator().Apply(state, batch);

    var nextState = Assert.IsType<InMemoryGameState>(application.State);
    var player = nextState.Players["player-1"];
    Assert.Empty(player.Zones.Deck);
    Assert.Single(player.Zones.Hand);
    Assert.Equal(card.CardInstanceId, player.Zones.Hand[0].CardInstanceId);
    Assert.Equal(_hasher.HashGameplayState(nextState), application.AppliedBatch.PostStateHash);
    Assert.Single(application.AppliedBatch.AppliedMutations);
  }

  [Fact]
  public void MoveCardZoneMutationRejectsMissingSourceCard()
  {
    var card = CreateCard("card-instance-1");
    var state = CreateState(new PlayerZones(hand: [card]));
    var batch = CreateMoveCardBatch(state, card.CardInstanceId, SourceZone.Deck, SourceZone.Hand);

    Assert.Throws<InvalidOperationException>(() => new StateMutator().Apply(state, batch));
  }

  [Fact]
  public void AdvanceTurnMutationUsesDeterministicPlayerOrder()
  {
    var state = CreateState(PlayerZones.Empty, activePlayerId: "player-2", turnNumber: 3, phase: "TURN_START");
    var batch = CreateAdvanceTurnBatch(state, "player-2");

    var application = new StateMutator().Apply(state, batch);

    Assert.Equal("player-1", application.State.ActivePlayerId);
    Assert.Equal(4, application.State.TurnNumber);
    Assert.Equal("TURN_START", application.State.Phase);
    Assert.Equal(1, application.State.Players["player-1"].MaxMana);
    Assert.Equal(1, application.State.Players["player-1"].Mana);
    Assert.Equal(_hasher.HashGameplayState(application.State), application.AppliedBatch.PostStateHash);
  }

  [Fact]
  public void AdvanceTurnMutationRefillsNextPlayerManaUpToCap()
  {
    var state = CreateState(
      PlayerZones.Empty,
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START",
      player2Mana: 0,
      player2MaxMana: 9);
    var batch = CreateAdvanceTurnBatch(state, "player-1");

    var application = new StateMutator().Apply(state, batch);

    Assert.Equal("player-2", application.State.ActivePlayerId);
    Assert.Equal(10, application.State.Players["player-2"].MaxMana);
    Assert.Equal(10, application.State.Players["player-2"].Mana);
  }

  [Fact]
  public void AdvanceTurnMutationRejectsNonActivePlayer()
  {
    var state = CreateState(PlayerZones.Empty, activePlayerId: "player-1", turnNumber: 1, phase: "TURN_START");
    var batch = CreateAdvanceTurnBatch(state, "player-2");

    Assert.Throws<InvalidOperationException>(() => new StateMutator().Apply(state, batch));
    Assert.Equal("player-1", state.ActivePlayerId);
  }

  [Fact]
  public void ModifyManaMutationUpdatesPlayerManaThroughStateMutator()
  {
    var state = CreateState(PlayerZones.Empty, mana: 5);
    var batch = CreateModifyManaBatch(state, "player-1", amount: -2);

    var application = new StateMutator().Apply(state, batch);

    Assert.Equal(3, application.State.Players["player-1"].Mana);
    Assert.Equal(5, state.Players["player-1"].Mana);
  }

  [Fact]
  public void ModifyManaMutationRejectsNegativeResult()
  {
    var state = CreateState(PlayerZones.Empty, mana: 1);
    var batch = CreateModifyManaBatch(state, "player-1", amount: -2);

    Assert.Throws<InvalidOperationException>(() => new StateMutator().Apply(state, batch));
    Assert.Equal(1, state.Players["player-1"].Mana);
  }

  [Fact]
  public void ApplyModifierMutationAddsPlayerCostModifierToGameplayState()
  {
    var state = CreateState(PlayerZones.Empty, mana: 5);
    var batch = CreateApplyCostModifierBatch(state, "player-1", "modifier-1", amount: 2);

    var application = new StateMutator().Apply(state, batch);

    var modifier = Assert.Single(application.State.Players["player-1"].Modifiers);
    Assert.Equal("modifier-1", modifier.ModifierId);
    Assert.Equal(PlayerModifierTypes.CostModifier, modifier.ModifierType);
    Assert.Equal(2, modifier.Amount);
    Assert.NotEqual(_hasher.HashGameplayState(state), _hasher.HashGameplayState(application.State));
  }

  [Fact]
  public void CreateChoiceMutationAddsPendingChoiceToGameplayState()
  {
    var state = CreateState(PlayerZones.Empty);
    var batch = CreateChoiceBatch(state, "player-1", "choice-discover-1");

    var application = new StateMutator().Apply(state, batch);
    var choice = Assert.Single(application.State.PendingChoices.Values);

    Assert.Equal("choice-discover-1", choice.ChoiceId);
    Assert.Equal("player-1", choice.PlayerId);
    Assert.Equal("DISCOVER", choice.ChoiceType);
    Assert.Equal("HAND", choice.ResultZone);
    Assert.Equal(["card:arcane-a", "card:arcane-b"], choice.Options.Select(option => option.ItemId));
    Assert.NotEqual(_hasher.HashGameplayState(state), _hasher.HashGameplayState(application.State));
  }

  [Fact]
  public void ResolveChoiceMutationRemovesPendingChoiceFromGameplayState()
  {
    var initialState = CreateState(PlayerZones.Empty);
    var state = new StateMutator()
      .Apply(initialState, CreateChoiceBatch(initialState, "player-1", "choice-discover-1"))
      .State;
    var selectedOptionId = state.PendingChoices["choice-discover-1"].Options[0].OptionId;
    var batch = CreateResolveChoiceBatch(state, "player-1", "choice-discover-1", selectedOptionId);

    var application = new StateMutator().Apply(state, batch);
    var committed = new CommittedEventFactory().CreateCommittedEvents(application.AppliedBatch, 1);

    Assert.Empty(application.State.PendingChoices);
    Assert.NotEqual(_hasher.HashGameplayState(state), _hasher.HashGameplayState(application.State));
    Assert.Equal(CommittedEventType.ChoiceResolved, Assert.Single(committed).EventType);
  }

  [Fact]
  public void ResolveChoiceMutationRejectsInvalidOption()
  {
    var initialState = CreateState(PlayerZones.Empty);
    var state = new StateMutator()
      .Apply(initialState, CreateChoiceBatch(initialState, "player-1", "choice-discover-1"))
      .State;
    var batch = CreateResolveChoiceBatch(state, "player-1", "choice-discover-1", "missing-option");

    Assert.Throws<InvalidOperationException>(() => new StateMutator().Apply(state, batch));
    Assert.Single(state.PendingChoices);
  }

  [Fact]
  public void CreateCardInstanceMutationAddsCardToRequestedZone()
  {
    var state = CreateState(PlayerZones.Empty);
    var batch = CreateCardInstanceBatch(
      state,
      "player-1",
      "card-discovered-1",
      "card:arcane-a",
      "card:arcane-a:v1",
      "HAND");

    var application = new StateMutator().Apply(state, batch);
    var card = Assert.Single(application.State.Players["player-1"].Zones.Hand);

    Assert.Equal("card-discovered-1", card.CardInstanceId);
    Assert.Equal("card:arcane-a", card.CardId);
    Assert.Equal("card:arcane-a:v1", card.CardVersion);
    Assert.NotEqual(_hasher.HashGameplayState(state), _hasher.HashGameplayState(application.State));
  }

  [Fact]
  public void RemoveModifierMutationRemovesPlayerCostModifier()
  {
    var state = CreateState(
      PlayerZones.Empty,
      mana: 5,
      modifiers: [CreateCostModifier("modifier-1", amount: 2)]);
    var batch = CreateRemoveModifierBatch(state, "player-1", "modifier-1");

    var application = new StateMutator().Apply(state, batch);

    Assert.Empty(application.State.Players["player-1"].Modifiers);
  }

  [Fact]
  public void UpdateQuestProgressMutationCompletesQuestAndCreatesCommittedEvent()
  {
    var quest = new PlayerQuest(
      "quest-play-minions",
      "quest-card-instance-1",
      "player-1",
      "MINION_SUMMONED",
      Progress: 0,
      RequiredProgress: 1,
      IsCompleted: false,
      RewardEffectId: "effect_quest_reward_draw",
      SourceCardVersion: "quest-card-v1");
    var state = CreateState(PlayerZones.Empty, quests: [quest]);
    var batch = CreateQuestProgressBatch(state, "player-1", quest.QuestId);

    var application = new StateMutator().Apply(state, batch);
    var committed = new CommittedEventFactory().CreateCommittedEvents(application.AppliedBatch, 1);

    var updatedQuest = Assert.Single(application.State.Players["player-1"].Quests ?? []);
    Assert.Equal(1, updatedQuest.Progress);
    Assert.True(updatedQuest.IsCompleted);
    Assert.NotEqual(_hasher.HashGameplayState(state), _hasher.HashGameplayState(application.State));
    Assert.Equal(CommittedEventType.QuestProgressChanged, Assert.Single(committed).EventType);
  }

  private InMemoryGameState CreateState(
    PlayerZones zones,
    string? activePlayerId = null,
    int turnNumber = 0,
    string phase = "BOOTSTRAP",
    int mana = 0,
    int maxMana = 0,
    int player2Mana = 0,
    int player2MaxMana = 0,
    IReadOnlyList<PlayerModifier>? modifiers = null,
    IReadOnlyList<PlayerQuest>? quests = null)
  {
    var player = PlayerState.Empty("player-1") with
    {
      Mana = mana,
      MaxMana = maxMana,
      Modifiers = modifiers ?? [],
      Quests = quests ?? [],
      Zones = zones
    };
    var player2 = PlayerState.Empty("player-2") with
    {
      Mana = player2Mana,
      MaxMana = player2MaxMana
    };

    return new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        [player.PlayerId] = player,
        [player2.PlayerId] = player2
      },
      activePlayerId: activePlayerId,
      turnNumber: turnNumber,
      phase: phase);
  }

  private ProposedMutationBatch CreateMoveCardBatch(
    IGameState state,
    string cardInstanceId,
    SourceZone fromZone,
    SourceZone toZone)
  {
    var mutation = new StateMutation(
      $"mutation:move:{cardInstanceId}",
      MoveCardZoneMutationHandler.SupportedMutationType,
      "test",
      cardInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal),
      "TEST",
      state.Revision + 1,
      null,
      $"move:{cardInstanceId}",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["cardInstanceId"] = cardInstanceId,
        ["fromZone"] = ToCanonicalZone(fromZone),
        ["playerId"] = "player-1",
        ["toZone"] = ToCanonicalZone(toZone)
      });

    return new ProposedMutationBatch(
      $"batch:move:{cardInstanceId}",
      state.Revision + 1,
      null,
      "TEST",
      "test",
      "MOVE_CARD_ZONE_TEST",
      [mutation],
      _hasher.HashGameplayState(state),
      null,
      null);
  }

  private ProposedMutationBatch CreateModifyManaBatch(
    IGameState state,
    string playerId,
    int amount)
  {
    var mutation = new StateMutation(
      $"mutation:modify-mana:{playerId}",
      ModifyManaMutationHandler.SupportedMutationType,
      "test",
      playerId,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = amount
      },
      "TEST",
      state.Revision + 1,
      null,
      $"modify-mana:{playerId}:0001",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["playerId"] = playerId
      });

    return new ProposedMutationBatch(
      $"batch:modify-mana:{playerId}",
      state.Revision + 1,
      null,
      "TEST",
      "test",
      "MODIFY_MANA_TEST",
      [mutation],
      _hasher.HashGameplayState(state),
      null,
      null);
  }

  private ProposedMutationBatch CreateApplyCostModifierBatch(
    IGameState state,
    string playerId,
    string modifierId,
    int amount)
  {
    var mutation = new StateMutation(
      $"mutation:apply-modifier:{modifierId}",
      ApplyModifierMutationHandler.SupportedMutationType,
      "test",
      playerId,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = amount
      },
      "TEST",
      state.Revision + 1,
      null,
      $"apply-modifier:{modifierId}:0001",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["duration"] = PlayerModifierDurations.NextPlay,
        ["modifierId"] = modifierId,
        ["modifierType"] = PlayerModifierTypes.CostModifier,
        ["playerId"] = playerId,
        ["priority"] = "0",
        ["sourceRef"] = "test"
      });

    return new ProposedMutationBatch(
      $"batch:apply-modifier:{modifierId}",
      state.Revision + 1,
      null,
      "TEST",
      "test",
      "APPLY_MODIFIER_TEST",
      [mutation],
      _hasher.HashGameplayState(state),
      null,
      null);
  }

  private ProposedMutationBatch CreateRemoveModifierBatch(
    IGameState state,
    string playerId,
    string modifierId)
  {
    var mutation = new StateMutation(
      $"mutation:remove-modifier:{modifierId}",
      RemoveModifierMutationHandler.SupportedMutationType,
      "test",
      playerId,
      new SortedDictionary<string, int>(StringComparer.Ordinal),
      "TEST",
      state.Revision + 1,
      null,
      $"remove-modifier:{modifierId}:0001",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["modifierId"] = modifierId,
        ["playerId"] = playerId
      });

    return new ProposedMutationBatch(
      $"batch:remove-modifier:{modifierId}",
      state.Revision + 1,
      null,
      "TEST",
      "test",
      "REMOVE_MODIFIER_TEST",
      [mutation],
      _hasher.HashGameplayState(state),
      null,
      null);
  }

  private ProposedMutationBatch CreateQuestProgressBatch(
    IGameState state,
    string playerId,
    string questId)
  {
    var mutation = new StateMutation(
      $"mutation:update-quest-progress:{questId}",
      UpdateQuestProgressMutationHandler.SupportedMutationType,
      "test",
      playerId,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = 1
      },
      "QUEST_PROGRESS",
      state.Revision + 1,
      null,
      $"update-quest-progress:{questId}:0001",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["playerId"] = playerId,
        ["questId"] = questId,
        ["sourceEventType"] = "MINION_SUMMONED"
      });

    return new ProposedMutationBatch(
      $"batch:update-quest-progress:{questId}",
      state.Revision + 1,
      null,
      "QUEST_PROGRESS",
      "test",
      "UPDATE_QUEST_PROGRESS_TEST",
      [mutation],
      _hasher.HashGameplayState(state),
      null,
      null);
  }

  private ProposedMutationBatch CreateChoiceBatch(
    IGameState state,
    string playerId,
    string choiceId)
  {
    var mutation = new StateMutation(
      $"mutation:create-choice:{choiceId}",
      CreateChoiceMutationHandler.SupportedMutationType,
      "test",
      playerId,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["optionCount"] = 2
      },
      "TEST",
      state.Revision + 1,
      null,
      $"create-choice:{choiceId}:0001",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["choiceId"] = choiceId,
        ["choiceType"] = "DISCOVER",
        ["option:0001:id"] = $"{choiceId}:option:0001",
        ["option:0001:itemId"] = "card:arcane-a",
        ["option:0001:itemKind"] = "CARD",
        ["option:0002:id"] = $"{choiceId}:option:0002",
        ["option:0002:itemId"] = "card:arcane-b",
        ["option:0002:itemKind"] = "CARD",
        ["playerId"] = playerId,
        ["resultZone"] = "HAND",
        ["visibilityPolicy"] = "PUBLIC"
      });

    return new ProposedMutationBatch(
      $"batch:create-choice:{choiceId}",
      state.Revision + 1,
      null,
      "TEST",
      "test",
      "CREATE_CHOICE_TEST",
      [mutation],
      _hasher.HashGameplayState(state),
      null,
      null);
  }

  private ProposedMutationBatch CreateResolveChoiceBatch(
    IGameState state,
    string playerId,
    string choiceId,
    string selectedOptionId)
  {
    var mutation = new StateMutation(
      $"mutation:resolve-choice:{choiceId}",
      ResolveChoiceMutationHandler.SupportedMutationType,
      "test",
      playerId,
      new SortedDictionary<string, int>(StringComparer.Ordinal),
      "TEST",
      state.Revision + 1,
      null,
      $"resolve-choice:{choiceId}:0001",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["choiceId"] = choiceId,
        ["playerId"] = playerId,
        ["selectedOptionId"] = selectedOptionId
      });

    return new ProposedMutationBatch(
      $"batch:resolve-choice:{choiceId}",
      state.Revision + 1,
      null,
      "TEST",
      "test",
      "RESOLVE_CHOICE_TEST",
      [mutation],
      _hasher.HashGameplayState(state),
      null,
      null);
  }

  private ProposedMutationBatch CreateCardInstanceBatch(
    IGameState state,
    string playerId,
    string cardInstanceId,
    string cardId,
    string cardVersion,
    string toZone)
  {
    var mutation = new StateMutation(
      $"mutation:create-card:{cardInstanceId}",
      CreateCardInstanceMutationHandler.SupportedMutationType,
      "test",
      cardInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal),
      "TEST",
      state.Revision + 1,
      null,
      $"create-card:{cardInstanceId}:0001",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["cardId"] = cardId,
        ["cardInstanceId"] = cardInstanceId,
        ["cardType"] = "SPELL_CARD",
        ["cardVersion"] = cardVersion,
        ["playerId"] = playerId,
        ["toZone"] = toZone
      });

    return new ProposedMutationBatch(
      $"batch:create-card:{cardInstanceId}",
      state.Revision + 1,
      null,
      "TEST",
      "test",
      "CREATE_CARD_INSTANCE_TEST",
      [mutation],
      _hasher.HashGameplayState(state),
      null,
      null);
  }

  private ProposedMutationBatch CreateAdvanceTurnBatch(
    IGameState state,
    string activePlayerId)
  {
    var mutation = new StateMutation(
      $"mutation:advance-turn:{activePlayerId}",
      AdvanceTurnMutationHandler.SupportedMutationType,
      "test",
      activePlayerId,
      new SortedDictionary<string, int>(StringComparer.Ordinal),
      "TURN_END",
      state.Revision + 1,
      null,
      $"advance-turn:{activePlayerId}:0001",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["activePlayerId"] = activePlayerId
      });

    return new ProposedMutationBatch(
      $"batch:advance-turn:{activePlayerId}",
      state.Revision + 1,
      null,
      "TURN_END",
      "test",
      "ADVANCE_TURN_TEST",
      [mutation],
      _hasher.HashGameplayState(state),
      null,
      null);
  }

  private static CardInstance CreateCard(string cardInstanceId)
  {
    return new CardInstance(
      cardInstanceId,
      "card:river-crocolisk",
      "1.0.0",
      "player-1",
      CardType.MinionCard);
  }

  private static PlayerModifier CreateCostModifier(string modifierId, int amount) =>
    new(
      modifierId,
      PlayerModifierTypes.CostModifier,
      amount,
      PlayerModifierDurations.NextPlay,
      Priority: 0,
      SourceRef: "test");

  private static string ToCanonicalZone(SourceZone zone)
  {
    return zone switch
    {
      SourceZone.Deck => "DECK",
      SourceZone.Hand => "HAND",
      SourceZone.Graveyard => "GRAVEYARD",
      SourceZone.SetAside => "SET_ASIDE",
      _ => throw new InvalidOperationException($"Unsupported test card zone '{zone}'.")
    };
  }
}

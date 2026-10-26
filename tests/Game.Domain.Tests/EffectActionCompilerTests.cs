using System.Text.Json;
using Game.Domain.Events;
using Game.Domain.Effects;
using Game.Domain.Hashing;
using Game.Domain.History;
using Game.Domain.Mutation;
using Game.Domain.Pooling;
using Game.Domain.Randomness;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Domain.Targeting;
using Game.Shared.Dsl;
using Game.Shared.Events;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Domain.Tests;

public sealed class EffectActionCompilerTests
{
  private readonly IStateHasher _hasher = new StateHasher(new CanonicalJsonSerializer());

  [Fact]
  public void FixedDamageEffectCompilesMutationBatchWithoutMutatingState()
  {
    var target = CreateMinion("entity-opponent-a", "player-2");
    var state = CreateState(opponentBoard: [target]);
    var effect = CreateEffect(
      "effect-battlecry-damage",
      [new DealDamageActionDefinition(
        "action-damage",
        Fixed(3),
        new TargetRef("TARGET"),
        DamageSchool: null,
        Lifesteal: false,
        Preventable: true)]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", target.EntityInstanceId)]));

    Assert.True(result.IsAccepted);
    Assert.Equal(0, target.Damage);
    var mutation = Assert.Single(result.Batch!.Mutations);
    Assert.Equal(ApplyDamageMutationHandler.SupportedMutationType, mutation.MutationType);
    Assert.Equal(target.EntityInstanceId, mutation.TargetRef);
    Assert.Equal(3, mutation.Payload["amount"]);
    Assert.Equal(_hasher.HashGameplayState(state), result.Batch.PreStateHash);
  }

  [Fact]
  public void CompiledDamageBatchAppliesThroughStateMutator()
  {
    var target = CreateMinion("entity-opponent-a", "player-2");
    var state = CreateState(opponentBoard: [target]);
    var effect = CreateEffect(
      "effect-battlecry-damage",
      [new DealDamageActionDefinition(
        "action-damage",
        Fixed(2),
        new TargetRef("TARGET"),
        DamageSchool: null,
        Lifesteal: false,
        Preventable: true)]);
    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", target.EntityInstanceId)]));

    var application = new StateMutator().Apply(state, result.Batch!);

    Assert.Equal(0, state.Players["player-2"].Zones.Board.Single().Damage);
    Assert.Equal(2, application.State.Players["player-2"].Zones.Board.Single().Damage);
    Assert.NotEqual(result.Batch!.PreStateHash, application.AppliedBatch.PostStateHash);
  }

  [Fact]
  public void DamageDistributionSourceOrderCompilesSeparateDamageInstances()
  {
    var first = CreateMinion("entity-opponent-a", "player-2");
    var second = CreateMinion("entity-opponent-b", "player-2");
    var state = CreateState(opponentBoard: [second, first]);
    var effect = CreateEffect(
      "effect-distribution",
      [new DamageDistributionActionDefinition(
        "action-distribute",
        Fixed(3),
        new EntityQuery("OPPONENT", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
        "SOURCE_ORDER",
        "SEPARATE_DAMAGE_INSTANCES",
        "NO_LIFESTEAL",
        "ALLOW_OVERKILL",
        FizzleIfNoTargets: false,
        "NONE")]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));

    Assert.True(result.IsAccepted);
    Assert.Equal(
      [first.EntityInstanceId, second.EntityInstanceId, first.EntityInstanceId],
      result.Batch!.Mutations.Select(mutation => mutation.TargetRef));
    Assert.All(result.Batch.Mutations, mutation =>
    {
      Assert.Equal(ApplyDamageMutationHandler.SupportedMutationType, mutation.MutationType);
      Assert.Equal(1, mutation.Payload["amount"]);
    });

    var application = new StateMutator().Apply(state, result.Batch);
    var appliedBoard = application.State.Players["player-2"].Zones.Board;
    Assert.Equal(2, appliedBoard.Single(entity => entity.EntityInstanceId == first.EntityInstanceId).Damage);
    Assert.Equal(1, appliedBoard.Single(entity => entity.EntityInstanceId == second.EntityInstanceId).Damage);
  }

  [Fact]
  public void DamageDistributionRandomSplitEmitsRngEvidence()
  {
    var first = CreateMinion("entity-opponent-a", "player-2");
    var second = CreateMinion("entity-opponent-b", "player-2");
    var state = CreateState(opponentBoard: [first, second]);
    var effect = CreateEffect(
      "effect-random-distribution",
      [new DamageDistributionActionDefinition(
        "action-random-distribute",
        Fixed(2),
        new EntityQuery("OPPONENT", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
        "RANDOM_SPLIT",
        "SEPARATE_DAMAGE_INSTANCES",
        "NO_LIFESTEAL",
        "ALLOW_OVERKILL",
        FizzleIfNoTargets: false,
        "RANDOM_TARGET")]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      rngRoller: new DeterministicRngRoller(new GameplayRandomStream(123))));

    Assert.True(result.IsAccepted);
    Assert.Equal(2, result.Batch!.Mutations.Count);
    Assert.Equal(2, result.RngRolls.Count);
    Assert.All(result.RngRolls, roll =>
    {
      Assert.Equal(RngStreamKind.RandomTarget, roll.Roll.Stream);
      Assert.Equal("TARGET_SELECTION", roll.Roll.Reason);
      Assert.Single(roll.SelectedTargets);
    });
  }

  [Fact]
  public void DamageDistributionAggregatesLifestealIntoSourceControllerHeroHeal()
  {
    var target = CreateMinion("entity-opponent-a", "player-2");
    var damagedHero = CreateHero("hero-player-1", "player-1") with { Damage = 4 };
    var state = CreateState(opponentBoard: [target], selfHero: damagedHero);
    var effect = CreateEffect(
      "effect-distribution",
      [new DamageDistributionActionDefinition(
        "action-distribute",
        Fixed(3),
        new EntityQuery("OPPONENT", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
        "SOURCE_ORDER",
        "SEPARATE_DAMAGE_INSTANCES",
        "AGGREGATE_LIFESTEAL",
        "ALLOW_OVERKILL",
        FizzleIfNoTargets: false,
        "NONE")]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));
    var application = new StateMutator().Apply(state, result.Batch!);

    Assert.True(result.IsAccepted);
    Assert.Equal(
      [ApplyDamageMutationHandler.SupportedMutationType, ApplyDamageMutationHandler.SupportedMutationType, ApplyDamageMutationHandler.SupportedMutationType, HealEntityMutationHandler.SupportedMutationType],
      result.Batch!.Mutations.Select(mutation => mutation.MutationType));
    Assert.Equal(3, result.Batch.Mutations.Last().Payload["amount"]);
    Assert.Equal(1, application.State.Players["player-1"].Hero!.Damage);
  }

  [Fact]
  public void DamageDistributionCanCapOverkillAtRemainingHealth()
  {
    var target = CreateMinion("entity-opponent-a", "player-2") with { Health = 2, Damage = 1 };
    var state = CreateState(opponentBoard: [target]);
    var effect = CreateEffect(
      "effect-distribution",
      [new DamageDistributionActionDefinition(
        "action-distribute",
        Fixed(3),
        new EntityQuery("OPPONENT", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
        "SOURCE_ORDER",
        "SEPARATE_DAMAGE_INSTANCES",
        "NO_LIFESTEAL",
        "CAP_AT_REMAINING_HEALTH",
        FizzleIfNoTargets: false,
        "NONE")]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));
    var application = new StateMutator().Apply(state, result.Batch!);

    Assert.True(result.IsAccepted);
    var mutation = Assert.Single(result.Batch!.Mutations);
    Assert.Equal(1, mutation.Payload["amount"]);
    Assert.Equal("CAP_AT_REMAINING_HEALTH", mutation.Metadata!["overkillPolicy"]);
    Assert.Equal(2, application.State.Players["player-2"].Zones.Board.Single().Damage);
  }

  [Fact]
  public void DamageDistributionSkipsMissingTargetWhenItDisappearsBeforeApplication()
  {
    var target = CreateMinion("entity-opponent-a", "player-2");
    var state = CreateState(opponentBoard: [target]);
    var effect = CreateEffect(
      "effect-distribution",
      [new DamageDistributionActionDefinition(
        "action-distribute",
        Fixed(1),
        new EntityQuery("OPPONENT", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
        "SOURCE_ORDER",
        "SEPARATE_DAMAGE_INSTANCES",
        "NO_LIFESTEAL",
        "ALLOW_OVERKILL",
        FizzleIfNoTargets: false,
        "NONE")]);
    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));
    var destroy = new StateMutation(
      "mutation:1:effect:effect-distribution:destroy-before-damage",
      DestroyEntityMutationHandler.SupportedMutationType,
      "test",
      target.EntityInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal),
      "ON_PLAY",
      1,
      null,
      "0000",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["entityInstanceId"] = target.EntityInstanceId,
        ["playerId"] = "player-2"
      });
    var batch = result.Batch! with { Mutations = [destroy, .. result.Batch.Mutations] };

    var application = new StateMutator().Apply(state, batch);

    Assert.Empty(application.State.Players["player-2"].Zones.Board);
    Assert.Equal([DestroyEntityMutationHandler.SupportedMutationType], application.AppliedBatch.AppliedMutations.Select(mutation => mutation.MutationType));
  }

  [Fact]
  public void DamageDistributionRejectsUnsupportedLifestealPolicyBeforeMutation()
  {
    var target = CreateMinion("entity-opponent-a", "player-2");
    var state = CreateState(opponentBoard: [target]);
    var effect = CreateEffect(
      "effect-distribution",
      [new DamageDistributionActionDefinition(
        "action-distribute",
        Fixed(3),
        new EntityQuery("OPPONENT", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
        "SOURCE_ORDER",
        "SEPARATE_DAMAGE_INSTANCES",
        "HEAL_PER_INSTANCE",
        "ALLOW_OVERKILL",
        FizzleIfNoTargets: false,
        "NONE")]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));

    Assert.False(result.IsAccepted);
    Assert.Equal(EffectActionCompilationDiagnosticCode.UnsupportedValueExpression, result.Diagnostics.Single().Code);
  }

  [Fact]
  public void ApplyAuraActionRefreshesAuraStateThroughMutationPipeline()
  {
    var sourceCard = CreateMinionCard("card:aura-source", "player-1");
    var source = CreateMinion("entity-aura-source", "player-1") with
    {
      SourceCardInstanceId = sourceCard.CardInstanceId
    };
    var state = CreateState(selfBoard: [source], selfGraveyard: [sourceCard]);
    var effect = CreateEffect(
      "effect-apply-aura",
      [new ApplyAuraActionDefinition(
        "action-apply-aura",
        PlayerSelector.Self,
        new AuraDefinition(
          "aura-board-attack",
          "BOARD_STAT_AURA",
          new EntityQuery("SELF", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
          new ModifierDefinition(
            "modifier-aura-attack",
            PlayerModifierTypes.BoardAttackModifier,
            Fixed(1),
            PlayerModifierDurations.WhileSourceActive,
            Priority: 10),
          PlayerModifierDurations.WhileSourceActive,
          Priority: 10))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: null,
      rngRoller: null,
      pools: null,
      maxBoardSize: null,
      historyStore: null) with
      {
        Source = new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-1", source.EntityInstanceId)
      });
    var application = new StateMutator().Apply(state, result.Batch!);
    var committed = new CommittedEventFactory().CreateCommittedEvents(application.AppliedBatch, 1);

    Assert.True(result.IsAccepted);
    var mutation = Assert.Single(result.Batch!.Mutations);
    Assert.Equal(RefreshAurasMutationHandler.SupportedMutationType, mutation.MutationType);
    Assert.Equal("aura-board-attack", mutation.Metadata!["aura:0001:auraId"]);
    Assert.Equal(PlayerModifierDurations.WhileSourceActive, mutation.Metadata["duration"]);
    Assert.Equal(PlayerModifierDurations.WhileSourceActive, mutation.Metadata["modifierDuration"]);
    var aura = Assert.Single(application.State.Players["player-1"].Auras!);
    Assert.Equal(source.EntityInstanceId, aura.SourceEntityInstanceId);
    Assert.Equal(CommittedEventType.AuraRefreshed, Assert.Single(committed).EventType);
  }

  [Fact]
  public void ReplaceEventActionCompilesDamageReplacementModifier()
  {
    var target = CreateMinion("entity-target", "player-2");
    var state = CreateState(opponentBoard: [target]);
    var effect = CreateEffect(
      "effect-replace-event",
      [new ReplaceEventActionDefinition(
        "action-replace-damage",
        "DAMAGE_WOULD_BE_DEALT",
        new TargetRef("TARGET"),
        "PREVENT_DAMAGE",
        PlayerModifierDurations.NextDamage,
        "replacement-prevent-next")]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", target.EntityInstanceId)]));
    var application = new StateMutator().Apply(state, result.Batch!);

    Assert.True(result.IsAccepted);
    var mutation = Assert.Single(result.Batch!.Mutations);
    Assert.Equal(ApplyModifierMutationHandler.SupportedMutationType, mutation.MutationType);
    Assert.Equal(PlayerModifierTypes.DamagePrevention, mutation.Metadata!["modifierType"]);
    Assert.Equal("DAMAGE_WOULD_BE_DEALT", mutation.Metadata["eventType"]);
    Assert.Equal("PREVENT_DAMAGE", mutation.Metadata["replacementPolicy"]);
    var modifier = Assert.Single(application.State.Players["player-2"].Modifiers);
    Assert.Equal("replacement-prevent-next", modifier.ModifierId);
    Assert.Equal(int.MaxValue, modifier.Amount);
  }

  [Fact]
  public void TakeExtraTurnActionSchedulesExtraTurnThroughMutationPipeline()
  {
    var state = CreateState();
    var effect = CreateEffect(
      "effect-extra-turn",
      [new TakeExtraTurnActionDefinition(
        "action-extra-turn",
        PlayerSelector.Self,
        OncePerGame: true,
        CurrentTurnEndsNormally: true,
        "AFTER_CURRENT_TURN",
        "QUEUE_AFTER_CURRENT_TURN",
        CommandButtonReplacement: null,
        "EXPIRE_CURRENT_TURN_ONLY")]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));
    var application = new StateMutator().Apply(state, result.Batch!);
    var committed = new CommittedEventFactory().CreateCommittedEvents(application.AppliedBatch, firstEventSeq: 1);

    Assert.True(result.IsAccepted);
    var mutation = Assert.Single(result.Batch!.Mutations);
    Assert.Equal(ScheduleExtraTurnMutationHandler.SupportedMutationType, mutation.MutationType);
    Assert.Equal("player-1", mutation.TargetRef);
    Assert.Equal("player-1", mutation.Metadata!["playerId"]);
    Assert.Contains(application.State.Counters, counter =>
      counter.Key.StartsWith("extra-turn:queue:", StringComparison.Ordinal) && counter.Value == 1);
    Assert.Equal(1, application.State.Counters[ExtraTurnSchedule.UsedKey("player-1")]);
    Assert.Equal(CommittedEventType.ExtraTurnScheduled, Assert.Single(committed).EventType);
  }

  [Fact]
  public void TakeExtraTurnActionRejectsUnsupportedDurationInteractionPolicy()
  {
    var state = CreateState();
    var effect = CreateEffect(
      "effect-extra-turn",
      [new TakeExtraTurnActionDefinition(
        "action-extra-turn",
        PlayerSelector.Self,
        OncePerGame: true,
        CurrentTurnEndsNormally: true,
        "AFTER_CURRENT_TURN",
        "QUEUE_AFTER_CURRENT_TURN",
        CommandButtonReplacement: null,
        "EXPIRE_WITH_ORIGINAL_TURN")]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));

    Assert.False(result.IsAccepted);
    Assert.Equal(EffectActionCompilationDiagnosticCode.UnsupportedValueExpression, Assert.Single(result.Diagnostics).Code);
  }

  [Fact]
  public void UnsatisfiedEffectConditionProducesAcceptedEmptyBatch()
  {
    var target = CreateMinion("entity-opponent-a", "player-2");
    var state = CreateState(opponentBoard: [target]);
    var effect = CreateEffect(
      "effect-conditioned-damage",
      [new DealDamageActionDefinition(
        "action-damage",
        Fixed(3),
        new TargetRef("TARGET"),
        DamageSchool: null,
        Lifesteal: false,
        Preventable: true)],
      [CreateCondition("PLAYER_HEALTH_LESS_OR_EQUAL", new { controller = "SELF", threshold = 1 })]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", target.EntityInstanceId)]));

    Assert.True(result.IsAccepted);
    Assert.Empty(result.Batch!.Mutations);
    Assert.Equal(0, state.Players["player-2"].Zones.Board.Single().Damage);
  }

  [Fact]
  public void UnsupportedActionDefinitionIsRejectedExplicitly()
  {
    var state = CreateState();
    var effect = CreateEffect(
      "effect-add-card",
      [new AddCardToHandActionDefinition(
        "action-add-card",
        new CardSource("CATALOG_REF", "card-token"),
        PlayerSelector.Self,
        "FIZZLE",
        ResultRef: null)]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));

    Assert.False(result.IsAccepted);
    var diagnostic = Assert.Single(result.Diagnostics);
    Assert.Equal(EffectActionCompilationDiagnosticCode.UnsupportedAction, diagnostic.Code);
    Assert.Equal("action-add-card", diagnostic.ActionId);
  }

  [Fact]
  public void RefBasedDamageAmountIsRejectedExplicitly()
  {
    var target = CreateMinion("entity-opponent-a", "player-2");
    var state = CreateState(opponentBoard: [target]);
    var effect = CreateEffect(
      "effect-ref-damage",
      [new DealDamageActionDefinition(
        "action-damage",
        new ValueExpression("REF", FixedValue: null, Ref: "spell_power"),
        new TargetRef("TARGET"),
        DamageSchool: null,
        Lifesteal: false,
        Preventable: true)]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", target.EntityInstanceId)]));

    Assert.False(result.IsAccepted);
    Assert.Equal(
      EffectActionCompilationDiagnosticCode.UnsupportedValueExpression,
      Assert.Single(result.Diagnostics).Code);
  }

  [Fact]
  public void FixedDrawActionCompilesFirstDeckCardDeterministically()
  {
    var firstDeckCard = CreateCard("card-deck-1", "player-1");
    var secondDeckCard = CreateCard("card-deck-2", "player-1");
    var state = CreateState(selfDeck: [firstDeckCard, secondDeckCard]);
    var effect = CreateEffect(
      "effect-draw",
      [new DrawCardActionDefinition(
        "action-draw",
        PlayerSelector.Self,
        Fixed(1),
        "FIZZLE",
        ResultRef: null)]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));
    var application = new StateMutator().Apply(state, result.Batch!);

    var mutation = Assert.Single(result.Batch!.Mutations);
    Assert.Equal(DrawCardMutationHandler.SupportedMutationType, mutation.MutationType);
    Assert.Equal(firstDeckCard.CardInstanceId, mutation.TargetRef);
    Assert.Equal(firstDeckCard.CardInstanceId, application.State.Players["player-1"].Zones.Hand.Single().CardInstanceId);
    Assert.Equal(secondDeckCard.CardInstanceId, application.State.Players["player-1"].Zones.Deck.Single().CardInstanceId);
  }

  [Fact]
  public void FixedDrawActionSkipsWhenDeckIsEmptyAndPolicyFizzle()
  {
    var state = CreateState();
    var effect = CreateEffect(
      "effect-draw-empty",
      [new DrawCardActionDefinition(
        "action-draw",
        PlayerSelector.Self,
        Fixed(1),
        "FIZZLE",
        ResultRef: null)]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));

    Assert.True(result.IsAccepted);
    Assert.Empty(result.Batch!.Mutations);
  }

  [Fact]
  public void FixedDrawActionCompilesFatigueWhenDeckIsEmptyAndPolicyFatigue()
  {
    var state = CreateState();
    state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = state.Players["player-1"] with
        {
          FatigueCounter = 2
        },
        ["player-2"] = state.Players["player-2"]
      });
    var effect = CreateEffect(
      "effect-draw-fatigue",
      [new DrawCardActionDefinition(
        "action-draw",
        PlayerSelector.Self,
        Fixed(1),
        "FATIGUE",
        ResultRef: null)]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));
    var application = new StateMutator().Apply(state, result.Batch!);

    Assert.Equal(
      [ModifyFatigueMutationHandler.SupportedMutationType, ApplyDamageMutationHandler.SupportedMutationType],
      result.Batch!.Mutations.Select(mutation => mutation.MutationType));
    Assert.Equal(3, application.State.Players["player-1"].FatigueCounter);
    Assert.Equal(3, application.State.Players["player-1"].Hero?.Damage);
  }

  [Fact]
  public void FixedHealActionCompilesAndAppliesToHeroThroughMutationPipeline()
  {
    var damagedHero = CreateHero("hero-player-1", "player-1") with { Damage = 8 };
    var state = CreateState(selfHero: damagedHero);
    var effect = CreateEffect(
      "effect-heal-hero",
      [new HealActionDefinition(
        "action-heal",
        Fixed(3),
        new TargetRef("TARGET"))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.HeroKind, "player-1", damagedHero.EntityInstanceId)]));
    var application = new StateMutator().Apply(state, result.Batch!);
    var committed = new CommittedEventFactory().CreateCommittedEvents(application.AppliedBatch, 1);

    var mutation = Assert.Single(result.Batch!.Mutations);
    Assert.Equal(HealEntityMutationHandler.SupportedMutationType, mutation.MutationType);
    Assert.Equal(3, mutation.Payload["amount"]);
    Assert.Equal(5, application.State.Players["player-1"].Hero?.Damage);
    Assert.Equal(CommittedEventType.EntityHealed, Assert.Single(committed).EventType);
  }

  [Fact]
  public void FullyHealActionUsesCurrentDamageAsHealAmountAndSkipsUndamagedTargets()
  {
    var damaged = CreateMinion("entity-opponent-a", "player-2") with { Damage = 4 };
    var undamaged = CreateMinion("entity-opponent-b", "player-2");
    var state = CreateState(opponentBoard: [damaged, undamaged]);
    var effect = CreateEffect(
      "effect-full-heal",
      [new FullyHealActionDefinition(
        "action-full-heal",
        new TargetRef("TARGET"))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets:
      [
        new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", damaged.EntityInstanceId),
        new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", undamaged.EntityInstanceId)
      ]));
    var application = new StateMutator().Apply(state, result.Batch!);

    var mutation = Assert.Single(result.Batch!.Mutations);
    Assert.Equal(4, mutation.Payload["amount"]);
    Assert.All(application.State.Players["player-2"].Zones.Board, entity => Assert.Equal(0, entity.Damage));
  }

  [Fact]
  public void DestroyActionCompilesAndMovesSourceCardToGraveyard()
  {
    var sourceCard = CreateCard("card-entity-opponent-a", "player-2");
    var target = CreateMinion("entity-opponent-a", "player-2") with
    {
      SourceCardInstanceId = sourceCard.CardInstanceId
    };
    var state = CreateState(opponentBoard: [target], opponentSetAside: [sourceCard]);
    var effect = CreateEffect(
      "effect-destroy",
      [new DestroyActionDefinition(
        "action-destroy",
        new TargetRef("TARGET"),
        "FIZZLE_WHOLE_EFFECT")]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", target.EntityInstanceId)]));
    var application = new StateMutator().Apply(state, result.Batch!);
    var committed = new CommittedEventFactory().CreateCommittedEvents(application.AppliedBatch, 1);

    Assert.Equal(DestroyEntityMutationHandler.SupportedMutationType, Assert.Single(result.Batch!.Mutations).MutationType);
    Assert.Empty(application.State.Players["player-2"].Zones.Board);
    Assert.Equal(sourceCard.CardInstanceId, application.State.Players["player-2"].Zones.Graveyard.Single().CardInstanceId);
    Assert.Equal(CommittedEventType.EntityDestroyed, Assert.Single(committed).EventType);
  }

  [Fact]
  public void BatchDestroyActionCompilesMultipleDestroyMutationsInResolvedOrder()
  {
    var first = CreateMinion("entity-opponent-a", "player-2");
    var second = CreateMinion("entity-opponent-b", "player-2");
    var state = CreateState(opponentBoard: [first, second]);
    var effect = CreateEffect(
      "effect-batch-destroy",
      [new BatchDestroyActionDefinition(
        "action-batch-destroy",
        new TargetRef("TARGETS"),
        "SIMULTANEOUS",
        "FIZZLE_WHOLE_EFFECT")]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets:
      [
        new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", first.EntityInstanceId),
        new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", second.EntityInstanceId)
      ]));
    var application = new StateMutator().Apply(state, result.Batch!);

    Assert.Equal(
      [DestroyEntityMutationHandler.SupportedMutationType, DestroyEntityMutationHandler.SupportedMutationType],
      result.Batch!.Mutations.Select(mutation => mutation.MutationType));
    Assert.Equal(
      ["effect:effect-batch-destroy:0001:0001", "effect:effect-batch-destroy:0001:0002"],
      result.Batch.Mutations.Select(mutation => mutation.OrderingKey));
    Assert.Empty(application.State.Players["player-2"].Zones.Board);
  }

  [Fact]
  public void BatchDestroyActionSkipsInvalidTargetsAndResolvesPartial()
  {
    var first = CreateMinion("entity-opponent-a", "player-2");
    var second = CreateMinion("entity-opponent-b", "player-2");
    var dead = CreateMinion("entity-opponent-dead", "player-2") with
    {
      Health = 2,
      Damage = 2
    };
    var state = CreateState(opponentBoard: [first, dead, second]);
    var effect = CreateEffect(
      "effect-batch-destroy-partial",
      [new BatchDestroyActionDefinition(
        "action-batch-destroy",
        new TargetRef("TARGETS"),
        "SIMULTANEOUS",
        "SKIP_INVALID_TARGETS")]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets:
      [
        new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", first.EntityInstanceId),
        new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", "missing-target"),
        new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", dead.EntityInstanceId),
        new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", second.EntityInstanceId)
      ]));
    var application = new StateMutator().Apply(state, result.Batch!);

    Assert.True(result.IsAccepted);
    Assert.Equal(
      [first.EntityInstanceId, second.EntityInstanceId],
      result.Batch!.Mutations.Select(mutation => mutation.TargetRef));
    Assert.Equal(
      ["effect:effect-batch-destroy-partial:0001:0001", "effect:effect-batch-destroy-partial:0001:0002"],
      result.Batch.Mutations.Select(mutation => mutation.OrderingKey));
    Assert.Equal([dead.EntityInstanceId], application.State.Players["player-2"].Zones.Board.Select(entity => entity.EntityInstanceId));
  }

  [Fact]
  public void BatchDestroyActionSkipsAllInvalidTargetsAsEmptyPartialResolution()
  {
    var target = CreateMinion("entity-opponent-a", "player-2");
    var state = CreateState(opponentBoard: [target]);
    var effect = CreateEffect(
      "effect-batch-destroy-empty-partial",
      [new BatchDestroyActionDefinition(
        "action-batch-destroy",
        new TargetRef("TARGETS"),
        "SIMULTANEOUS",
        "SKIP_INVALID_TARGETS")]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets:
      [
        new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", "missing-target"),
        new ResolvedTargetRef(ResolvedTargetRef.HeroKind, "player-2", "hero-player-2")
      ]));

    Assert.True(result.IsAccepted);
    Assert.Empty(result.Batch!.Mutations);
  }

  [Fact]
  public void RandomSelectCanDriveBrawlLikeBatchDestroyWithRngEvidence()
  {
    var selfA = CreateMinion("entity-self-a", "player-1");
    var selfB = CreateMinion("entity-self-b", "player-1");
    var opponentA = CreateMinion("entity-opponent-a", "player-2");
    var opponentB = CreateMinion("entity-opponent-b", "player-2");
    var state = CreateState(
      selfBoard: [selfB, selfA],
      opponentBoard: [opponentB, opponentA]);
    var effect = CreateEffect(
      "effect-brawl",
      [
        new RandomSelectActionDefinition(
          "action-select-survivor",
          new EntityQuery(
            "ANY",
            [SourceZone.Board],
            [EntityType.Minion],
            RequiredTags: [],
            ExcludedTags: [],
            AliveOnly: true),
          new CountSpec(1, 1),
          "survivor",
          "RANDOM_TARGET",
          "FIZZLE_WHOLE_EFFECT"),
        new BatchDestroyActionDefinition(
          "action-destroy-others",
          new TargetRef("ALL_MINIONS_EXCEPT(ACTION_RESULT:action-select-survivor)"),
          "SIMULTANEOUS",
          "SKIP_INVALID_TARGETS")
      ]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      rngRoller: new DeterministicRngRoller(new GameplayRandomStream(123))));
    var application = new StateMutator().Apply(state, result.Batch!);
    var committed = new CommittedEventFactory().CreateCommittedEvents(application.AppliedBatch, 1);
    var survivor = Assert.Single(result.RngRolls).SelectedTargets.Single();

    Assert.True(result.IsAccepted);
    Assert.Equal(RngStreamKind.RandomTarget, result.RngRolls.Single().Roll.Stream);
    Assert.Equal(4, result.RngRolls.Single().Roll.CandidateCount);
    Assert.Equal(3, result.Batch!.Mutations.Count);
    Assert.DoesNotContain(result.Batch.Mutations, mutation => mutation.TargetRef == survivor.TargetId);
    Assert.Equal(
      [survivor.TargetId],
      application.State.Players.Values
        .SelectMany(player => player.Zones.Board)
        .Select(entity => entity.EntityInstanceId)
        .Order(StringComparer.Ordinal)
        .ToArray());
    Assert.All(committed, @event => Assert.Equal(CommittedEventType.EntityDestroyed, @event.EventType));
  }

  [Fact]
  public void RandomSelectRejectsEmptyCandidateSetWhenPolicyFizzles()
  {
    var state = CreateState();
    var effect = CreateEffect(
      "effect-random-empty",
      [
        new RandomSelectActionDefinition(
          "action-select",
          new EntityQuery(
            "ANY",
            [SourceZone.Board],
            [EntityType.Minion],
            RequiredTags: [],
            ExcludedTags: [],
            AliveOnly: true),
          new CountSpec(1, 1),
          "selected",
          "RANDOM_TARGET",
          "FIZZLE_WHOLE_EFFECT")
      ]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      rngRoller: new DeterministicRngRoller(new GameplayRandomStream(123))));

    Assert.False(result.IsAccepted);
    Assert.Equal(EffectActionCompilationDiagnosticCode.MissingTarget, result.Diagnostics.Single().Code);
    Assert.Empty(result.RngRolls);
  }

  [Fact]
  public void DiscardCardActionMovesExplicitHandCardToGraveyard()
  {
    var handCard = CreateCard("card-hand-1", "player-1");
    var state = CreateState(selfHand: [handCard]);
    var effect = CreateEffect(
      "effect-discard",
      [new DiscardCardActionDefinition(
        "action-discard",
        new TargetRef("TARGET"),
        SourceZone.Hand,
        "FIZZLE_WHOLE_EFFECT")]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.CardKind, "player-1", handCard.CardInstanceId)]));
    var application = new StateMutator().Apply(state, result.Batch!);

    var mutation = Assert.Single(result.Batch!.Mutations);
    Assert.Equal(MoveCardZoneMutationHandler.SupportedMutationType, mutation.MutationType);
    Assert.Equal("HAND", mutation.Metadata!["fromZone"]);
    Assert.Equal("GRAVEYARD", mutation.Metadata["toZone"]);
    Assert.Empty(application.State.Players["player-1"].Zones.Hand);
    Assert.Equal(handCard.CardInstanceId, application.State.Players["player-1"].Zones.Graveyard.Single().CardInstanceId);
  }

  [Fact]
  public void MoveToZoneAndShuffleIntoDeckActionsMoveExplicitCardTargets()
  {
    var graveyardCard = CreateCard("card-graveyard-1", "player-1");
    var handCard = CreateCard("card-hand-1", "player-1");
    var moveState = CreateState(selfHand: [handCard], selfGraveyard: [graveyardCard]);
    var moveEffect = CreateEffect(
      "effect-move",
      [new MoveToZoneActionDefinition(
        "action-move",
        new TargetRef("TARGET"),
        SourceZone.Deck,
        "KEEP_OWNER",
        "PUBLIC",
        ResultRef: null)]);
    var shuffleEffect = CreateEffect(
      "effect-shuffle",
      [new ShuffleIntoDeckActionDefinition(
        "action-shuffle",
        new TargetRef("TARGET"),
        "DETERMINISTIC_APPEND",
        ResultRef: null)]);

    var moveResult = new EffectActionCompiler().Compile(CreateRequest(
      moveState,
      moveEffect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.CardKind, "player-1", handCard.CardInstanceId)]));
    var moved = new StateMutator().Apply(moveState, moveResult.Batch!).State;
    var shuffleResult = new EffectActionCompiler().Compile(CreateRequest(
      moved,
      shuffleEffect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.CardKind, "player-1", graveyardCard.CardInstanceId)]));
    var shuffled = new StateMutator().Apply(moved, shuffleResult.Batch!).State;

    Assert.Equal("HAND", Assert.Single(moveResult.Batch!.Mutations).Metadata!["fromZone"]);
    Assert.Equal("DECK", Assert.Single(moveResult.Batch.Mutations).Metadata!["toZone"]);
    Assert.Equal("GRAVEYARD", Assert.Single(shuffleResult.Batch!.Mutations).Metadata!["fromZone"]);
    Assert.Equal(
      [handCard.CardInstanceId, graveyardCard.CardInstanceId],
      shuffled.Players["player-1"].Zones.Deck.Select(card => card.CardInstanceId));
  }

  [Fact]
  public void ReturnToHandActionRemovesBoardEntityAndMovesSourceCardToHand()
  {
    var sourceCard = CreateCard("card-entity-opponent-a", "player-2");
    var target = CreateMinion("entity-opponent-a", "player-2") with
    {
      SourceCardInstanceId = sourceCard.CardInstanceId
    };
    var state = CreateState(opponentBoard: [target], opponentSetAside: [sourceCard]);
    var effect = CreateEffect(
      "effect-return",
      [new ReturnToHandActionDefinition(
        "action-return",
        new TargetRef("TARGET"),
        "ORIGINAL_OWNER",
        "SKIP_ACTION",
        ResultRef: null)]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", target.EntityInstanceId)]));
    var application = new StateMutator().Apply(state, result.Batch!);
    var committed = new CommittedEventFactory().CreateCommittedEvents(application.AppliedBatch, 1);

    var mutation = Assert.Single(result.Batch!.Mutations);
    Assert.Equal(ReturnEntityToHandMutationHandler.SupportedMutationType, mutation.MutationType);
    Assert.Equal(target.EntityInstanceId, mutation.TargetRef);
    Assert.Equal(sourceCard.CardInstanceId, mutation.Metadata!["cardInstanceId"]);
    Assert.Equal("SET_ASIDE", mutation.Metadata["fromZone"]);
    Assert.Equal("HAND", mutation.Metadata["toZone"]);
    Assert.Empty(application.State.Players["player-2"].Zones.Board);
    Assert.Empty(application.State.Players["player-2"].Zones.SetAside);
    Assert.Equal(sourceCard.CardInstanceId, application.State.Players["player-2"].Zones.Hand.Single().CardInstanceId);
    Assert.Equal(CommittedEventType.CardReturnedToHand, Assert.Single(committed).EventType);
  }

  [Fact]
  public void ReturnToHandActionRejectsBoardEntityWithoutSetAsideSourceCard()
  {
    var target = CreateMinion("entity-opponent-a", "player-2");
    var state = CreateState(opponentBoard: [target]);
    var effect = CreateEffect(
      "effect-return-missing-source",
      [new ReturnToHandActionDefinition(
        "action-return",
        new TargetRef("TARGET"),
        "ORIGINAL_OWNER",
        "SKIP_ACTION",
        ResultRef: null)]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", target.EntityInstanceId)]));

    Assert.False(result.IsAccepted);
    Assert.Equal(EffectActionCompilationDiagnosticCode.MissingTarget, Assert.Single(result.Diagnostics).Code);
  }

  [Fact]
  public void ApplyModifierActionCompilesNextPlayCostModifierThroughMutationPipeline()
  {
    var state = CreateState();
    var effect = CreateEffect(
      "effect-cost-reduction",
      [new ApplyModifierActionDefinition(
        "action-cost-reduction",
        PlayerSelector.Self,
        new ModifierDefinition(
          "modifier-next-spell",
          "COST_MODIFIER",
          Fixed(2),
          "NEXT_PLAY",
          Priority: 10))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));
    var application = new StateMutator().Apply(state, result.Batch!);
    var committed = new CommittedEventFactory().CreateCommittedEvents(application.AppliedBatch, 1);

    var mutation = Assert.Single(result.Batch!.Mutations);
    Assert.Equal(ApplyModifierMutationHandler.SupportedMutationType, mutation.MutationType);
    Assert.Equal("player-1", mutation.TargetRef);
    Assert.Equal(2, mutation.Payload["amount"]);
    Assert.Equal("modifier-next-spell", mutation.Metadata!["modifierId"]);
    Assert.Equal("COST_MODIFIER", mutation.Metadata["modifierType"]);
    Assert.Equal("NEXT_PLAY", mutation.Metadata["duration"]);
    Assert.Equal("10", mutation.Metadata["priority"]);

    var modifier = Assert.Single(application.State.Players["player-1"].Modifiers);
    Assert.Equal("modifier-next-spell", modifier.ModifierId);
    Assert.Equal(2, modifier.Amount);
    Assert.Equal(PlayerModifierTypes.CostModifier, modifier.ModifierType);
    Assert.Equal(PlayerModifierDurations.NextPlay, modifier.Duration);
    Assert.Equal(CommittedEventType.ModifierApplied, Assert.Single(committed).EventType);
  }

  [Fact]
  public void ApplyModifierActionCompilesRestOfTurnCostModifierThroughMutationPipeline()
  {
    var state = CreateState();
    var effect = CreateEffect(
      "effect-turn-cost-reduction",
      [new ApplyModifierActionDefinition(
        "action-turn-cost-reduction",
        PlayerSelector.Self,
        new ModifierDefinition(
          "modifier-rest-of-turn",
          "COST_MODIFIER",
          Fixed(1),
          "REST_OF_TURN",
          Priority: 5))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));
    var application = new StateMutator().Apply(state, result.Batch!);

    var mutation = Assert.Single(result.Batch!.Mutations);
    Assert.Equal(ApplyModifierMutationHandler.SupportedMutationType, mutation.MutationType);
    Assert.Equal("REST_OF_TURN", mutation.Metadata!["duration"]);
    Assert.Equal("5", mutation.Metadata["priority"]);

    var modifier = Assert.Single(application.State.Players["player-1"].Modifiers);
    Assert.Equal("modifier-rest-of-turn", modifier.ModifierId);
    Assert.Equal(PlayerModifierTypes.CostModifier, modifier.ModifierType);
    Assert.Equal(PlayerModifierDurations.RestOfTurn, modifier.Duration);
  }

  [Fact]
  public void ApplyModifierActionCompilesNextDamagePreventionModifierThroughMutationPipeline()
  {
    var state = CreateState();
    var effect = CreateEffect(
      "effect-damage-prevention",
      [new ApplyModifierActionDefinition(
        "action-prevent-damage",
        PlayerSelector.Self,
        new ModifierDefinition(
          "modifier-prevent-next-damage",
          "DAMAGE_PREVENTION",
          Fixed(3),
          "NEXT_DAMAGE",
          Priority: 20))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));
    var application = new StateMutator().Apply(state, result.Batch!);

    var mutation = Assert.Single(result.Batch!.Mutations);
    Assert.Equal(ApplyModifierMutationHandler.SupportedMutationType, mutation.MutationType);
    Assert.Equal(3, mutation.Payload["amount"]);
    Assert.Equal("DAMAGE_PREVENTION", mutation.Metadata!["modifierType"]);
    Assert.Equal("NEXT_DAMAGE", mutation.Metadata["duration"]);
    Assert.Equal("20", mutation.Metadata["priority"]);

    var modifier = Assert.Single(application.State.Players["player-1"].Modifiers);
    Assert.Equal("modifier-prevent-next-damage", modifier.ModifierId);
    Assert.Equal(3, modifier.Amount);
    Assert.Equal(PlayerModifierTypes.DamagePrevention, modifier.ModifierType);
    Assert.Equal(PlayerModifierDurations.NextDamage, modifier.Duration);
  }

  [Fact]
  public void PreventDamageActionCompilesNextDamagePreventionModifierThroughMutationPipeline()
  {
    var target = CreateMinion("entity-target", "player-2");
    var state = CreateState(opponentBoard: [target]);
    var effect = CreateEffect(
      "effect-prevent-damage",
      [new PreventDamageActionDefinition(
        "action-prevent-damage",
        new TargetRef("TARGET"),
        Fixed(4),
        PlayerModifierDurations.NextDamage,
        "PREVENT_UP_TO_AMOUNT",
        "modifier-prevent-target")]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", target.EntityInstanceId)]));
    var application = new StateMutator().Apply(state, result.Batch!);
    var committed = new CommittedEventFactory().CreateCommittedEvents(application.AppliedBatch, 1);

    var mutation = Assert.Single(result.Batch!.Mutations);
    Assert.Equal(ApplyModifierMutationHandler.SupportedMutationType, mutation.MutationType);
    Assert.Equal("player-2", mutation.TargetRef);
    Assert.Equal(4, mutation.Payload["amount"]);
    Assert.Equal(PlayerModifierTypes.DamagePrevention, mutation.Metadata!["modifierType"]);
    Assert.Equal(PlayerModifierDurations.NextDamage, mutation.Metadata["duration"]);
    Assert.Equal("modifier:1:effect:effect-prevent-damage:modifier-prevent-target", mutation.Metadata["modifierId"]);
    Assert.Equal(target.EntityInstanceId, mutation.Metadata["targetId"]);

    var modifier = Assert.Single(application.State.Players["player-2"].Modifiers);
    Assert.Equal("modifier:1:effect:effect-prevent-damage:modifier-prevent-target", modifier.ModifierId);
    Assert.Equal(4, modifier.Amount);
    Assert.Equal(PlayerModifierTypes.DamagePrevention, modifier.ModifierType);
    Assert.Equal(PlayerModifierDurations.NextDamage, modifier.Duration);
    Assert.Equal(CommittedEventType.ModifierApplied, Assert.Single(committed).EventType);
  }

  [Fact]
  public void PreventDamageActionRejectsUnsupportedDurationBeforeMutation()
  {
    var target = CreateMinion("entity-target", "player-2");
    var state = CreateState(opponentBoard: [target]);
    var effect = CreateEffect(
      "effect-prevent-damage",
      [new PreventDamageActionDefinition(
        "action-prevent-damage",
        new TargetRef("TARGET"),
        Fixed(4),
        PlayerModifierDurations.RestOfTurn,
        "PREVENT_UP_TO_AMOUNT",
        "modifier-prevent-target")]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", target.EntityInstanceId)]));

    Assert.False(result.IsAccepted);
    Assert.Equal(EffectActionCompilationDiagnosticCode.UnsupportedValueExpression, Assert.Single(result.Diagnostics).Code);
  }

  [Fact]
  public void ApplyModifierActionCompilesBoardAttackModifierThroughStatMutations()
  {
    var left = CreateMinion("entity-board-b", "player-1");
    var right = CreateMinion("entity-board-a", "player-1");
    var state = CreateState(selfBoard: [left, right]);
    var effect = CreateEffect(
      "effect-board-attack-buff",
      [new ApplyModifierActionDefinition(
        "action-board-attack-buff",
        PlayerSelector.Self,
        new ModifierDefinition(
          "modifier-board-attack",
          PlayerModifierTypes.BoardAttackModifier,
          Fixed(2),
          PlayerModifierDurations.Permanent,
          Priority: 10))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));
    var application = new StateMutator().Apply(state, result.Batch!);
    var committed = new CommittedEventFactory().CreateCommittedEvents(application.AppliedBatch, 1);

    Assert.True(result.IsAccepted);
    Assert.Equal(
      ["entity-board-a", "entity-board-b"],
      result.Batch!.Mutations.Select(mutation => mutation.TargetRef));
    Assert.All(result.Batch.Mutations, mutation =>
    {
      Assert.Equal(ModifyAttackMutationHandler.SupportedMutationType, mutation.MutationType);
      Assert.Equal(2, mutation.Payload["amount"]);
      Assert.Equal(PlayerModifierTypes.BoardAttackModifier, mutation.Metadata!["modifierType"]);
      Assert.Equal(PlayerModifierDurations.Permanent, mutation.Metadata["duration"]);
    });
    Assert.Equal(
      [4, 4],
      application.State.Players["player-1"].Zones.Board
        .OrderBy(entity => entity.EntityInstanceId, StringComparer.Ordinal)
        .Select(entity => entity.Attack));
    Assert.Equal(
      [CommittedEventType.ModifierApplied, CommittedEventType.ModifierApplied],
      committed.Select(@event => @event.EventType));
  }

  [Fact]
  public void ApplyModifierActionCompilesBoardHealthModifierThroughStatMutations()
  {
    var state = CreateState(selfBoard: [CreateMinion("entity-board-a", "player-1")]);
    var effect = CreateEffect(
      "effect-board-health-buff",
      [new ApplyModifierActionDefinition(
        "action-board-health-buff",
        PlayerSelector.Self,
        new ModifierDefinition(
          "modifier-board-health",
          PlayerModifierTypes.BoardHealthModifier,
          Fixed(3),
          PlayerModifierDurations.Permanent,
          Priority: 5))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));
    var application = new StateMutator().Apply(state, result.Batch!);

    var mutation = Assert.Single(result.Batch!.Mutations);
    Assert.Equal(ModifyHealthMutationHandler.SupportedMutationType, mutation.MutationType);
    Assert.Equal("entity-board-a", mutation.TargetRef);
    Assert.Equal(3, mutation.Payload["amount"]);
    Assert.Equal(8, application.State.Players["player-1"].Zones.Board.Single().Health);
    Assert.Equal(0, application.State.Players["player-1"].Zones.Board.Single().Damage);
  }

  [Fact]
  public void ApplyModifierActionCompilesBoardKeywordModifierThroughKeywordMutations()
  {
    var withTaunt = new EntityInstance(
      "entity-board-a",
      "card:entity-board-a",
      "player-1",
      EntityType.Minion,
      Attack: 2,
      Health: 5,
      Damage: 0,
      Keywords: ["TAUNT"]);
    var withoutTaunt = CreateMinion("entity-board-b", "player-1");
    var state = CreateState(selfBoard: [withoutTaunt, withTaunt]);
    var effect = CreateEffect(
      "effect-board-keyword-buff",
      [new ApplyModifierActionDefinition(
        "action-board-keyword-buff",
        PlayerSelector.Self,
        new ModifierDefinition(
          "modifier-board-keyword",
          PlayerModifierTypes.BoardKeywordModifier,
          new ValueExpression("KEYWORD", FixedValue: null, Ref: "TAUNT"),
          PlayerModifierDurations.Permanent,
          Priority: 10))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));
    var application = new StateMutator().Apply(state, result.Batch!);
    var committed = new CommittedEventFactory().CreateCommittedEvents(application.AppliedBatch, 1);

    var mutation = Assert.Single(result.Batch!.Mutations);
    Assert.Equal(AddKeywordMutationHandler.SupportedMutationType, mutation.MutationType);
    Assert.Equal("entity-board-b", mutation.TargetRef);
    Assert.Equal("TAUNT", mutation.Metadata!["keyword"]);
    Assert.Equal(PlayerModifierTypes.BoardKeywordModifier, mutation.Metadata["modifierType"]);
    Assert.Equal(PlayerModifierDurations.Permanent, mutation.Metadata["duration"]);
    Assert.Equal(["TAUNT"], application.State.Players["player-1"].Zones.Board
      .Single(entity => entity.EntityInstanceId == "entity-board-b")
      .Keywords);
    Assert.Equal(["TAUNT"], application.State.Players["player-1"].Zones.Board
      .Single(entity => entity.EntityInstanceId == "entity-board-a")
      .Keywords);
    Assert.Equal(CommittedEventType.ModifierApplied, Assert.Single(committed).EventType);
  }

  [Fact]
  public void ApplyModifierActionCompilesHandZoneModifierThroughZoneMutations()
  {
    var first = CreateCard("card-hand-b", "player-1");
    var second = CreateCard("card-hand-a", "player-1");
    var state = CreateState(selfHand: [first, second]);
    var effect = CreateEffect(
      "effect-hand-zone-modifier",
      [new ApplyModifierActionDefinition(
        "action-hand-zone-modifier",
        PlayerSelector.Self,
        new ModifierDefinition(
          null,
          PlayerModifierTypes.HandZoneModifier,
          new ValueExpression("ZONE", FixedValue: null, Ref: "GRAVEYARD"),
          PlayerModifierDurations.Permanent,
          Priority: 0))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));
    var application = new StateMutator().Apply(state, result.Batch!);
    var committed = new CommittedEventFactory().CreateCommittedEvents(application.AppliedBatch, 1);

    Assert.True(result.IsAccepted);
    Assert.Equal(
      [MoveCardZoneMutationHandler.SupportedMutationType, MoveCardZoneMutationHandler.SupportedMutationType],
      result.Batch!.Mutations.Select(mutation => mutation.MutationType));
    Assert.Equal(
      ["card-hand-a", "card-hand-b"],
      result.Batch.Mutations.Select(mutation => mutation.Metadata!["cardInstanceId"]));
    Assert.All(result.Batch.Mutations, mutation =>
    {
      Assert.Equal(PlayerModifierTypes.HandZoneModifier, mutation.Metadata!["modifierType"]);
      Assert.Equal(PlayerModifierDurations.Permanent, mutation.Metadata["duration"]);
      Assert.Equal("HAND", mutation.Metadata["fromZone"]);
      Assert.Equal("GRAVEYARD", mutation.Metadata["toZone"]);
    });
    Assert.Empty(application.State.Players["player-1"].Zones.Hand);
    Assert.Equal(
      ["card-hand-a", "card-hand-b"],
      application.State.Players["player-1"].Zones.Graveyard.Select(card => card.CardInstanceId));
    Assert.All(committed, @event => Assert.Equal(CommittedEventType.CardMovedZone, @event.EventType));
  }

  [Fact]
  public void ApplyModifierActionRejectsUnsupportedDurationBeforeMutation()
  {
    var state = CreateState();
    var effect = CreateEffect(
      "effect-permanent-modifier",
      [new ApplyModifierActionDefinition(
        "action-permanent-modifier",
        PlayerSelector.Self,
        new ModifierDefinition(
          "modifier-permanent",
          "COST_MODIFIER",
          Fixed(1),
          "PERMANENT",
          Priority: 0))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));

    Assert.False(result.IsAccepted);
    var diagnostic = Assert.Single(result.Diagnostics);
    Assert.Equal(EffectActionCompilationDiagnosticCode.UnsupportedValueExpression, diagnostic.Code);
    Assert.Equal("action-permanent-modifier", diagnostic.ActionId);
  }

  [Fact]
  public void DiscoverActionCreatesPendingChoiceFromFixedPool()
  {
    var state = CreateState();
    var effect = CreateEffect(
      "effect-discover",
      [new DiscoverActionDefinition(
        "action-discover",
        PlayerSelector.Self,
        new PoolRef("pool-spells"),
        OfferCount: 2,
        "PLAYER_CHOICE",
        SourceZone.Hand,
        "SKIP_ACTION",
        ResultRef: "choice-spell")]);
    var pools = new SortedDictionary<string, FixedPoolDefinition>(StringComparer.Ordinal)
    {
      ["pool-spells"] = new(
        "pool-spells",
        PoolResolver.FixedPoolType,
        [
          new PoolItemRef("card", "card:z-last"),
          new PoolItemRef("card", "card:a-first"),
          new PoolItemRef("card", "card:m-middle")
        ])
    };

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect, pools: pools));
    var application = new StateMutator().Apply(state, result.Batch!);
    var committed = new CommittedEventFactory().CreateCommittedEvents(application.AppliedBatch, 1);

    Assert.True(result.IsAccepted);
    var mutation = Assert.Single(result.Batch!.Mutations);
    Assert.Equal(CreateChoiceMutationHandler.SupportedMutationType, mutation.MutationType);
    Assert.Equal("player-1", mutation.TargetRef);
    Assert.Equal(2, mutation.Payload["optionCount"]);
    Assert.Equal("card:a-first", mutation.Metadata!["option:0001:itemId"]);
    Assert.Equal("card:m-middle", mutation.Metadata["option:0002:itemId"]);

    var choice = Assert.Single(application.State.PendingChoices.Values);
    Assert.Equal("choice-spell", choice.ChoiceId);
    Assert.Equal(["card:a-first", "card:m-middle"], choice.Options.Select(option => option.ItemId));
    Assert.Equal(CommittedEventType.ChoiceCreated, Assert.Single(committed).EventType);
  }

  [Fact]
  public void DiscoverActionRandomPoolCreatesRngLoggedOffersWithoutReplacement()
  {
    var state = CreateState();
    var effect = CreateEffect(
      "effect-discover-random",
      [new DiscoverActionDefinition(
        "action-discover",
        PlayerSelector.Self,
        new PoolRef("pool-spells"),
        OfferCount: 2,
        "PLAYER_CHOICE",
        SourceZone.Hand,
        "SKIP_ACTION",
        ResultRef: "choice-spell")]);
    var pools = new SortedDictionary<string, FixedPoolDefinition>(StringComparer.Ordinal)
    {
      ["pool-spells"] = new(
        "pool-spells",
        PoolResolver.FixedPoolType,
        [
          new PoolItemRef("card", "card:z-last"),
          new PoolItemRef("card", "card:a-first"),
          new PoolItemRef("card", "card:m-middle")
        ],
        Ordering: FixedPoolOrdering.Random)
    };

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      pools: pools,
      rngRoller: new DeterministicRngRoller(new GameplayRandomStream(123))));
    var repeat = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      pools: pools,
      rngRoller: new DeterministicRngRoller(new GameplayRandomStream(123))));

    Assert.True(result.IsAccepted);
    Assert.Equal(2, result.RngRolls.Count);
    Assert.All(result.RngRolls, roll => Assert.Equal(RngStreamKind.DiscoverOffer, roll.Roll.Stream));
    Assert.Equal([3, 2], result.RngRolls.Select(roll => roll.Roll.CandidateCount));
    Assert.Equal(
      result.Batch!.Mutations.Single().Metadata!.Where(pair => pair.Key.EndsWith(":itemId", StringComparison.Ordinal)),
      repeat.Batch!.Mutations.Single().Metadata!.Where(pair => pair.Key.EndsWith(":itemId", StringComparison.Ordinal)));
    Assert.Equal(
      result.RngRolls.Select(roll => roll.Roll.SelectedIndex),
      repeat.RngRolls.Select(roll => roll.Roll.SelectedIndex));

    var itemIds = result.Batch.Mutations.Single().Metadata!
      .Where(pair => pair.Key.EndsWith(":itemId", StringComparison.Ordinal))
      .Select(pair => pair.Value)
      .ToArray();
    Assert.Equal(2, itemIds.Distinct(StringComparer.Ordinal).Count());
  }

  [Fact]
  public void DiscoverActionRandomPoolRequiresRngRoller()
  {
    var state = CreateState();
    var effect = CreateEffect(
      "effect-discover-random",
      [new DiscoverActionDefinition(
        "action-discover",
        PlayerSelector.Self,
        new PoolRef("pool-spells"),
        OfferCount: 2,
        "PLAYER_CHOICE",
        SourceZone.Hand,
        "SKIP_ACTION",
        ResultRef: "choice-spell")]);
    var pools = new SortedDictionary<string, FixedPoolDefinition>(StringComparer.Ordinal)
    {
      ["pool-spells"] = new(
        "pool-spells",
        PoolResolver.FixedPoolType,
        [new PoolItemRef("card", "card:a-first")],
        Ordering: FixedPoolOrdering.Random)
    };

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect, pools: pools));

    Assert.False(result.IsAccepted);
    Assert.Equal(EffectActionCompilationDiagnosticCode.UnsupportedAction, Assert.Single(result.Diagnostics).Code);
  }

  [Fact]
  public void DiscoverActionSkipsWhenFixedPoolIsEmptyAndPolicySkips()
  {
    var state = CreateState();
    var effect = CreateEffect(
      "effect-discover-empty",
      [new DiscoverActionDefinition(
        "action-discover",
        PlayerSelector.Self,
        new PoolRef("pool-empty"),
        OfferCount: 3,
        "PLAYER_CHOICE",
        SourceZone.Hand,
        "SKIP_ACTION",
        ResultRef: null)]);
    var pools = new SortedDictionary<string, FixedPoolDefinition>(StringComparer.Ordinal)
    {
      ["pool-empty"] = new(
        "pool-empty",
        PoolResolver.FixedPoolType,
        [])
    };

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect, pools: pools));

    Assert.True(result.IsAccepted);
    Assert.Empty(result.Batch!.Mutations);
  }

  [Fact]
  public void DestroyActionRejectsHeroTargetBeforeMutation()
  {
    var state = CreateState();
    var effect = CreateEffect(
      "effect-destroy-hero",
      [new DestroyActionDefinition(
        "action-destroy",
        new TargetRef("TARGET"),
        "FIZZLE_WHOLE_EFFECT")]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.HeroKind, "player-2", "hero-player-2")]));

    Assert.False(result.IsAccepted);
    Assert.Equal(EffectActionCompilationDiagnosticCode.UnsupportedTarget, Assert.Single(result.Diagnostics).Code);
  }

  [Fact]
  public void ResurrectActionMovesGraveyardMinionToSetAsideAndSummonsEntity()
  {
    var card = CreateMinionCard("card-dead-minion-1", "player-1");
    var state = CreateState(selfGraveyard: [card]);
    var effect = CreateEffect(
      "effect-resurrect",
      [new ResurrectActionDefinition(
        "action-resurrect",
        new TargetRef("TARGET"),
        PlayerSelector.Self,
        Fixed(2),
        Fixed(3),
        "APPEND",
        "SKIP_ACTION",
        ResultRef: "resurrected-minion")]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.CardKind, "player-1", card.CardInstanceId)],
      maxBoardSize: 7));
    var application = new StateMutator().Apply(state, result.Batch!);
    var events = new CommittedEventFactory().CreateCommittedEvents(application.AppliedBatch, firstEventSeq: 1);

    Assert.True(result.IsAccepted);
    Assert.Equal(
      [MoveCardZoneMutationHandler.SupportedMutationType, SummonEntityMutationHandler.SupportedMutationType],
      result.Batch!.Mutations.Select(mutation => mutation.MutationType));
    Assert.Equal("7", result.Batch.Mutations[1].Metadata!["maxBoardSize"]);
    Assert.Empty(application.State.Players["player-1"].Zones.Graveyard);
    Assert.Contains(application.State.Players["player-1"].Zones.SetAside, moved =>
      StringComparer.Ordinal.Equals(moved.CardInstanceId, card.CardInstanceId));
    var entity = Assert.Single(application.State.Players["player-1"].Zones.Board);
    Assert.Equal($"entity:{card.CardInstanceId}:resurrect:000001:action-resurrect", entity.EntityInstanceId);
    Assert.Equal(card.CardInstanceId, entity.SourceCardInstanceId);
    Assert.Equal(2, entity.Attack);
    Assert.Equal(3, entity.Health);
    Assert.Collection(
      events,
      first => Assert.Equal(CommittedEventType.CardMovedZone, first.EventType),
      second => Assert.Equal(CommittedEventType.MinionSummoned, second.EventType));
  }

  [Fact]
  public void ResurrectActionRejectsNonMinionGraveyardCard()
  {
    var card = CreateCard("card-dead-spell-1", "player-1");
    var state = CreateState(selfGraveyard: [card]);
    var effect = CreateEffect(
      "effect-resurrect-spell",
      [new ResurrectActionDefinition(
        "action-resurrect",
        new TargetRef("TARGET"),
        PlayerSelector.Self,
        Fixed(2),
        Fixed(3),
        "APPEND",
        "SKIP_ACTION",
        ResultRef: null)]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.CardKind, "player-1", card.CardInstanceId)],
      maxBoardSize: 7));

    Assert.False(result.IsAccepted);
    Assert.Equal(EffectActionCompilationDiagnosticCode.UnsupportedTarget, Assert.Single(result.Diagnostics).Code);
  }

  [Fact]
  public void ResurrectActionSkipsWhenBoardIsFullAndPolicySkips()
  {
    var card = CreateMinionCard("card-dead-minion-1", "player-1");
    var state = CreateState(
      selfGraveyard: [card],
      selfBoard: [CreateMinion("entity-existing", "player-1")]);
    var effect = CreateEffect(
      "effect-resurrect-board-full-skip",
      [new ResurrectActionDefinition(
        "action-resurrect",
        new TargetRef("TARGET"),
        PlayerSelector.Self,
        Fixed(2),
        Fixed(3),
        "APPEND",
        "SKIP_ACTION",
        ResultRef: null)]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.CardKind, "player-1", card.CardInstanceId)],
      maxBoardSize: 1));

    Assert.True(result.IsAccepted);
    Assert.Empty(result.Batch!.Mutations);
  }

  [Fact]
  public void ResurrectActionRejectsWhenBoardIsFullAndPolicyFizzles()
  {
    var card = CreateMinionCard("card-dead-minion-1", "player-1");
    var state = CreateState(
      selfGraveyard: [card],
      selfBoard: [CreateMinion("entity-existing", "player-1")]);
    var effect = CreateEffect(
      "effect-resurrect-board-full-fizzle",
      [new ResurrectActionDefinition(
        "action-resurrect",
        new TargetRef("TARGET"),
        PlayerSelector.Self,
        Fixed(2),
        Fixed(3),
        "APPEND",
        "FIZZLE_WHOLE_EFFECT",
        ResultRef: null)]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      chosenTargets: [new ResolvedTargetRef(ResolvedTargetRef.CardKind, "player-1", card.CardInstanceId)],
      maxBoardSize: 1));

    Assert.False(result.IsAccepted);
    Assert.Equal(EffectActionCompilationDiagnosticCode.UnsupportedTarget, Assert.Single(result.Diagnostics).Code);
  }

  [Fact]
  public void ResurrectActionSelectsGraveyardMinionFromHistoryQueryWhenNoTargetIsSupplied()
  {
    var first = CreateMinionCard("card-dead-minion-1", "player-1");
    var last = CreateMinionCard("card-dead-minion-2", "player-1");
    var state = CreateState(selfGraveyard: [first, last]);
    var effect = CreateEffect(
      "effect-resurrect-history",
      [new ResurrectActionDefinition(
        "action-resurrect",
        new TargetRef("HISTORY_QUERY"),
        PlayerSelector.Self,
        Fixed(4),
        Fixed(5),
        "APPEND",
        "SKIP_ACTION",
        ResultRef: null,
        new HistoryQuerySpec(
          "OWN_GRAVEYARD_MINION",
          PlayerSelector.Self,
          "LAST",
          MaxCandidates: 10))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      maxBoardSize: 7));
    var application = new StateMutator().Apply(state, result.Batch!);

    Assert.True(result.IsAccepted);
    Assert.Equal(
      [MoveCardZoneMutationHandler.SupportedMutationType, SummonEntityMutationHandler.SupportedMutationType],
      result.Batch!.Mutations.Select(mutation => mutation.MutationType));
    Assert.Contains(application.State.Players["player-1"].Zones.Graveyard, card =>
      StringComparer.Ordinal.Equals(card.CardInstanceId, first.CardInstanceId));
    Assert.DoesNotContain(application.State.Players["player-1"].Zones.Graveyard, card =>
      StringComparer.Ordinal.Equals(card.CardInstanceId, last.CardInstanceId));
    var entity = Assert.Single(application.State.Players["player-1"].Zones.Board);
    Assert.Equal(last.CardInstanceId, entity.SourceCardInstanceId);
    Assert.Equal(4, entity.Attack);
    Assert.Equal(5, entity.Health);
  }

  [Fact]
  public void ResurrectActionSelectsOpponentGraveyardMinionFromHistoryQuery()
  {
    var first = CreateMinionCard("card-opponent-dead-minion-1", "player-2");
    var last = CreateMinionCard("card-opponent-dead-minion-2", "player-2");
    var state = CreateState(opponentGraveyard: [first, last]);
    var effect = CreateEffect(
      "effect-resurrect-opponent-history",
      [new ResurrectActionDefinition(
        "action-resurrect-opponent",
        new TargetRef("HISTORY_QUERY"),
        PlayerSelector.Opponent,
        Fixed(4),
        Fixed(5),
        "APPEND",
        "SKIP_ACTION",
        ResultRef: null,
        new HistoryQuerySpec(
          "OPPONENT_GRAVEYARD_MINION",
          PlayerSelector.Opponent,
          "LAST",
          MaxCandidates: 10))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      maxBoardSize: 7));
    var application = new StateMutator().Apply(state, result.Batch!);

    Assert.True(result.IsAccepted);
    Assert.Equal(
      [MoveCardZoneMutationHandler.SupportedMutationType, SummonEntityMutationHandler.SupportedMutationType],
      result.Batch!.Mutations.Select(mutation => mutation.MutationType));
    Assert.Contains(application.State.Players["player-2"].Zones.Graveyard, card =>
      StringComparer.Ordinal.Equals(card.CardInstanceId, first.CardInstanceId));
    Assert.DoesNotContain(application.State.Players["player-2"].Zones.Graveyard, card =>
      StringComparer.Ordinal.Equals(card.CardInstanceId, last.CardInstanceId));
    var entity = Assert.Single(application.State.Players["player-2"].Zones.Board);
    Assert.Equal(last.CardInstanceId, entity.SourceCardInstanceId);
    Assert.Equal("player-2", entity.ControllerPlayerId);
    Assert.Equal(4, entity.Attack);
    Assert.Equal(5, entity.Health);
  }

  [Fact]
  public void ResurrectActionSelectsAnyGraveyardMinionAndPreservesOwnerZoneMovement()
  {
    var ownDead = CreateMinionCard("card-own-dead-minion", "player-1");
    var opponentDead = CreateMinionCard("card-opponent-dead-minion", "player-2");
    var state = CreateState(
      selfGraveyard: [ownDead],
      opponentGraveyard: [opponentDead]);
    var effect = CreateEffect(
      "effect-resurrect-any-history",
      [new ResurrectActionDefinition(
        "action-resurrect-any",
        new TargetRef("HISTORY_QUERY"),
        PlayerSelector.Self,
        Fixed(4),
        Fixed(5),
        "APPEND",
        "SKIP_ACTION",
        ResultRef: null,
        new HistoryQuerySpec(
          "ANY_GRAVEYARD_MINION",
          PlayerSelector.Self,
          "LAST",
          MaxCandidates: 10))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      maxBoardSize: 7));
    var application = new StateMutator().Apply(state, result.Batch!);

    Assert.True(result.IsAccepted);
    Assert.Equal(
      [MoveCardZoneMutationHandler.SupportedMutationType, SummonEntityMutationHandler.SupportedMutationType],
      result.Batch!.Mutations.Select(mutation => mutation.MutationType));
    Assert.Contains(application.State.Players["player-1"].Zones.Graveyard, card =>
      StringComparer.Ordinal.Equals(card.CardInstanceId, ownDead.CardInstanceId));
    Assert.DoesNotContain(application.State.Players["player-2"].Zones.Graveyard, card =>
      StringComparer.Ordinal.Equals(card.CardInstanceId, opponentDead.CardInstanceId));
    Assert.Contains(application.State.Players["player-2"].Zones.SetAside, card =>
      StringComparer.Ordinal.Equals(card.CardInstanceId, opponentDead.CardInstanceId));
    var entity = Assert.Single(application.State.Players["player-1"].Zones.Board);
    Assert.Equal(opponentDead.CardInstanceId, entity.SourceCardInstanceId);
    Assert.Equal("player-1", entity.ControllerPlayerId);
    Assert.Equal(4, entity.Attack);
    Assert.Equal(5, entity.Health);
  }

  [Fact]
  public void ResurrectActionSelectsDeadMinionFromReplayHistoryQuery()
  {
    var first = CreateMinionCard("card-dead-minion-first", "player-1");
    var last = CreateMinionCard("card-dead-minion-last", "player-1");
    var state = CreateState(selfGraveyard: [first, last]);
    var historyStore = new HistoryStore().AppendCommittedEvents(
    [
      CreateEntityDiedEvent(10, "player-1", first.CardInstanceId, first.CardVersion),
      CreateEntityDiedEvent(11, "player-1", last.CardInstanceId, last.CardVersion)
    ]);
    var effect = CreateEffect(
      "effect-resurrect-dead-history",
      [new ResurrectActionDefinition(
        "action-resurrect-dead",
        new TargetRef("HISTORY_QUERY"),
        PlayerSelector.Self,
        Fixed(4),
        Fixed(5),
        "APPEND",
        "SKIP_ACTION",
        ResultRef: null,
        new HistoryQuerySpec(
          "ANY_DEAD_MINION",
          PlayerSelector.Self,
          "LAST",
          MaxCandidates: 10))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      historyStore: historyStore,
      maxBoardSize: 7));
    var application = new StateMutator().Apply(state, result.Batch!);

    Assert.True(result.IsAccepted);
    Assert.Equal(last.CardInstanceId, Assert.Single(application.State.Players["player-1"].Zones.Board).SourceCardInstanceId);
    Assert.Equal([first.CardInstanceId], application.State.Players["player-1"].Zones.Graveyard.Select(card => card.CardInstanceId));
  }

  [Fact]
  public void ResurrectActionRejectsDeadMinionHistoryQueryWithoutHistoryStore()
  {
    var state = CreateState(selfGraveyard: [CreateMinionCard("card-dead-minion", "player-1")]);
    var effect = CreateEffect(
      "effect-resurrect-dead-history-missing-context",
      [new ResurrectActionDefinition(
        "action-resurrect-dead",
        new TargetRef("HISTORY_QUERY"),
        PlayerSelector.Self,
        Fixed(4),
        Fixed(5),
        "APPEND",
        "SKIP_ACTION",
        ResultRef: null,
        new HistoryQuerySpec(
          "ANY_DEAD_MINION",
          PlayerSelector.Self,
          "LAST",
          MaxCandidates: 10))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));

    Assert.False(result.IsAccepted);
    Assert.Equal(EffectActionCompilationDiagnosticCode.UnsupportedValueExpression, Assert.Single(result.Diagnostics).Code);
  }

  [Fact]
  public void ResurrectActionSelectsHistoryQueryMinionWithGameplayRngEvidence()
  {
    var first = CreateMinionCard("card-dead-minion-1", "player-1");
    var second = CreateMinionCard("card-dead-minion-2", "player-1");
    var state = CreateState(selfGraveyard: [first, second]);
    var effect = CreateEffect(
      "effect-resurrect-history-random",
      [new ResurrectActionDefinition(
        "action-resurrect-random",
        new TargetRef("HISTORY_QUERY"),
        PlayerSelector.Self,
        Fixed(4),
        Fixed(5),
        "APPEND",
        "SKIP_ACTION",
        ResultRef: null,
        new HistoryQuerySpec(
          "OWN_GRAVEYARD_MINION",
          PlayerSelector.Self,
          "RANDOM",
          MaxCandidates: 10))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(
      state,
      effect,
      rngRoller: new DeterministicRngRoller(new GameplayRandomStream(42)),
      maxBoardSize: 7));
    var application = new StateMutator().Apply(state, result.Batch!);

    Assert.True(result.IsAccepted);
    var rngRoll = Assert.Single(result.RngRolls);
    Assert.Equal("HISTORY_QUERY", rngRoll.Roll.Reason);
    Assert.Equal(RngStreamKind.RandomTarget, rngRoll.Roll.Stream);
    var selectedCardId = Assert.Single(rngRoll.SelectedTargets).TargetId;
    Assert.DoesNotContain(application.State.Players["player-1"].Zones.Graveyard, card =>
      StringComparer.Ordinal.Equals(card.CardInstanceId, selectedCardId));
    var entity = Assert.Single(application.State.Players["player-1"].Zones.Board);
    Assert.Equal(selectedCardId, entity.SourceCardInstanceId);
  }

  [Fact]
  public void ResurrectActionSkipsHistoryQueryWhenNoEligibleCardExistsAndPolicySkips()
  {
    var state = CreateState(selfGraveyard: [CreateCard("card-dead-spell-1", "player-1")]);
    var effect = CreateEffect(
      "effect-resurrect-history-empty",
      [new ResurrectActionDefinition(
        "action-resurrect",
        new TargetRef("HISTORY_QUERY"),
        PlayerSelector.Self,
        Fixed(4),
        Fixed(5),
        "APPEND",
        "SKIP_ACTION",
        ResultRef: null,
        new HistoryQuerySpec(
          "OWN_GRAVEYARD_MINION",
          PlayerSelector.Self,
          "LAST",
          MaxCandidates: 10))]);

    var result = new EffectActionCompiler().Compile(CreateRequest(state, effect));

    Assert.True(result.IsAccepted);
    Assert.Empty(result.Batch!.Mutations);
  }

  private EffectActionCompilationRequest CreateRequest(
    IGameState state,
    EffectDefinition effect,
    IReadOnlyList<ResolvedTargetRef>? chosenTargets = null,
    IRngRoller? rngRoller = null,
    IReadOnlyDictionary<string, FixedPoolDefinition>? pools = null,
    int? maxBoardSize = null,
    HistoryStore? historyStore = null)
  {
    return new EffectActionCompilationRequest(
      state,
      effect,
      "player-1",
      "player-1",
      "card-instance-1",
      CommandSeq: 1,
      _hasher.HashGameplayState(state),
      chosenTargets,
      Pools: pools,
      RngRoller: rngRoller,
      MaxBoardSize: maxBoardSize,
      HistoryStore: historyStore);
  }

  private static CommittedEventRecord CreateEntityDiedEvent(
    long eventSeq,
    string playerId,
    string sourceCardInstanceId,
    string cardVersion) =>
    new(
      eventSeq,
      null,
      1,
      CommittedEventType.EntityDied,
      EventCategory.Committed,
      Phase.OnPlay,
      [new EventRef("SOURCE", sourceCardInstanceId, "BOARD", playerId)],
      [new EventRef("ENTITY_INSTANCE", $"entity:{sourceCardInstanceId}", "BOARD", playerId)],
      [
        new EventPayloadEntry("playerId", new EventPayloadValue("STRING", playerId, null, null, [])),
        new EventPayloadEntry("sourceCardInstanceId", new EventPayloadValue("STRING", sourceCardInstanceId, null, null, [])),
        new EventPayloadEntry("cardVersion", new EventPayloadValue("STRING", cardVersion, null, null, []))
      ],
      "batch-1",
      ["mutation-1"],
      [],
      "sha256:pre",
      "sha256:post",
      EventVisibility.Public);

  private static EffectDefinition CreateEffect(
    string effectId,
    IReadOnlyList<ActionDefinition> actions,
    IReadOnlyList<Condition>? conditions = null)
  {
    return new EffectDefinition(
      effectId,
      EffectKind.SimplePlayEffect,
      [SourceZone.Hand],
      Trigger: null,
      new TimingPolicy(Phase.OnPlay, Window: null, Priority: 0, OrderingPolicy: "DECLARED_ORDER"),
      conditions ?? [],
      TargetRequirement: null,
      actions,
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("FIZZLE"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: []);
  }

  private static ValueExpression Fixed(int value)
  {
    return new ValueExpression("FIXED", value, Ref: null);
  }

  private static Condition CreateCondition(string kind, object parameters)
  {
    return new Condition(kind, JsonSerializer.SerializeToElement(parameters));
  }

  private static InMemoryGameState CreateState(
    IReadOnlyList<CardInstance>? selfDeck = null,
    IReadOnlyList<CardInstance>? selfHand = null,
    IReadOnlyList<CardInstance>? selfGraveyard = null,
    IReadOnlyList<CardInstance>? opponentGraveyard = null,
    IReadOnlyList<EntityInstance>? selfBoard = null,
    IReadOnlyList<EntityInstance>? opponentBoard = null,
    IReadOnlyList<CardInstance>? opponentSetAside = null,
    EntityInstance? selfHero = null)
  {
    var player1 = PlayerState.Empty("player-1") with
    {
      Hero = selfHero ?? CreateHero("hero-player-1", "player-1"),
      Zones = new PlayerZones(
        deck: selfDeck ?? [],
        hand: selfHand ?? [],
        graveyard: selfGraveyard ?? [],
        board: selfBoard ?? [])
    };
    var player2 = PlayerState.Empty("player-2") with
    {
      Hero = CreateHero("hero-player-2", "player-2"),
      Zones = new PlayerZones(
        graveyard: opponentGraveyard ?? [],
        setAside: opponentSetAside ?? [],
        board: opponentBoard ?? [])
    };

    return new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        [player1.PlayerId] = player1,
        [player2.PlayerId] = player2
      });
  }

  private static EntityInstance CreateMinion(string entityInstanceId, string playerId)
  {
    return new EntityInstance(
      entityInstanceId,
      $"card:{entityInstanceId}",
      playerId,
      EntityType.Minion,
      Attack: 2,
      Health: 5,
      Damage: 0);
  }

  private static EntityInstance CreateHero(string entityInstanceId, string playerId)
  {
    return new EntityInstance(
      entityInstanceId,
      $"card:{entityInstanceId}",
      playerId,
      EntityType.Hero,
      Attack: 0,
      Health: 30,
      Damage: 0);
  }

  private static CardInstance CreateCard(string cardInstanceId, string playerId)
  {
    return new CardInstance(
      cardInstanceId,
      $"card:{cardInstanceId}",
      "1.0.0",
      playerId,
      CardType.SpellCard);
  }

  private static CardInstance CreateMinionCard(string cardInstanceId, string playerId)
  {
    return new CardInstance(
      cardInstanceId,
      $"card:{cardInstanceId}",
      "1.0.0",
      playerId,
      CardType.MinionCard);
  }
}

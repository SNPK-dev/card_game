using Game.Domain.Hashing;
using Game.Domain.Mutation;
using Game.Domain.Replacements;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Shared.Events;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Domain.Tests;

public sealed class ReplacementResolverTests
{
  private readonly IStateHasher _hasher = new StateHasher(new CanonicalJsonSerializer());

  [Fact]
  public void ResolveReturnsUnchangedBatchAndOrderedDecisions()
  {
    var state = new InMemoryGameState();
    var batch = CreateBatch(state);
    var resolver = new ReplacementResolver();
    var later = CreateProposedEvent("event-b", 11, ProposedEventType.EntityWouldDie);
    var earlier = CreateProposedEvent("event-a", 10, ProposedEventType.DamageWouldBeDealt);

    var result = resolver.Resolve(new ReplacementResolutionRequest(state, batch, [later, earlier]));

    Assert.True(result.IsAccepted);
    Assert.Equal(batch.BatchId, result.FinalBatch?.BatchId);
    Assert.Equal(batch.Mutations.Select(mutation => mutation.MutationId), result.FinalBatch?.Mutations.Select(mutation => mutation.MutationId));
    Assert.Equal(
      ["replacement-decision:0000000010:event-a", "replacement-decision:0000000011:event-b"],
      result.Decisions.Select(decision => decision.DecisionId));
    Assert.All(result.Decisions, decision =>
    {
      Assert.Equal(ReplacementDecisionResult.Unchanged, decision.Result);
    });
    Assert.Contains(result.Decisions, decision =>
      decision.ProposedEventId == "event-a" && decision.Reason == "NO_PREVENTION_AVAILABLE");
    Assert.Contains(result.Decisions, decision =>
      decision.ProposedEventId == "event-b" && decision.Reason == "NO_REPLACEMENT_FOR_PROPOSED_EVENT_TYPE");
    Assert.Equal(_hasher.HashGameplayState(state), batch.PreStateHash);
    Assert.Equal(0, state.Revision);
  }

  [Fact]
  public void ResolveRejectsLimitOverflowBeforeFinalMutationApplication()
  {
    var resolver = new ReplacementResolver();
    var state = new InMemoryGameState();
    var result = resolver.Resolve(new ReplacementResolutionRequest(
      state,
      CreateBatch(state),
      [CreateProposedEvent("event-a", 1), CreateProposedEvent("event-b", 2)],
      new ReplacementResolutionPolicy(MaxProposedEvents: 1)));

    Assert.False(result.IsAccepted);
    Assert.Null(result.FinalBatch);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == ReplacementResolutionDiagnosticCode.ProposedEventLimitExceeded);
  }

  [Fact]
  public void ResolveRejectsInvalidEventSequenceCandidate()
  {
    var resolver = new ReplacementResolver();
    var state = new InMemoryGameState();

    var result = resolver.Resolve(new ReplacementResolutionRequest(
      state,
      CreateBatch(state),
      [CreateProposedEvent("event-invalid", 0)]));

    Assert.False(result.IsAccepted);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == ReplacementResolutionDiagnosticCode.InvalidEventSequenceCandidate &&
      diagnostic.ProposedEventId == "event-invalid");
  }

  [Fact]
  public void ResolveConsumesDamagePreventionModifierAndReducesDamageMutation()
  {
    var state = CreateDamageState(preventionAmount: 2);
    var batch = CreateDamageBatch(state, amount: 5, preventable: true);
    var resolver = new ReplacementResolver();

    var result = resolver.Resolve(new ReplacementResolutionRequest(
      state,
      batch,
      [CreateProposedEvent("event-damage", 1, ProposedEventType.DamageWouldBeDealt)]));

    Assert.True(result.IsAccepted);
    var damage = Assert.Single(result.FinalBatch!.Mutations, mutation =>
      mutation.MutationType == ApplyDamageMutationHandler.SupportedMutationType);
    Assert.Equal(3, damage.Payload["amount"]);
    Assert.Equal("2", damage.Metadata?["preventedAmount"]);
    Assert.Contains(result.FinalBatch.Mutations, mutation =>
      mutation.MutationType == RemoveModifierMutationHandler.SupportedMutationType
      && mutation.Metadata?["modifierId"] == "prevent-1");
    var decision = Assert.Single(result.Decisions);
    Assert.Equal(ReplacementDecisionResult.Modified, decision.Result);
    Assert.Equal("DAMAGE_PREVENTED:2", decision.Reason);
  }

  [Fact]
  public void ResolveCancelsDamageMutationWhenPreventionCoversFullAmount()
  {
    var state = CreateDamageState(preventionAmount: 5);
    var batch = CreateDamageBatch(state, amount: 3, preventable: true);
    var resolver = new ReplacementResolver();

    var result = resolver.Resolve(new ReplacementResolutionRequest(
      state,
      batch,
      [CreateProposedEvent("event-damage", 1, ProposedEventType.DamageWouldBeDealt)]));

    Assert.True(result.IsAccepted);
    Assert.DoesNotContain(result.FinalBatch!.Mutations, mutation =>
      mutation.MutationType == ApplyDamageMutationHandler.SupportedMutationType);
    Assert.Contains(result.FinalBatch.Mutations, mutation =>
      mutation.MutationType == RemoveModifierMutationHandler.SupportedMutationType);
    Assert.Equal(ReplacementDecisionResult.Cancelled, Assert.Single(result.Decisions).Result);
  }

  [Fact]
  public void ResolveConsumesDivineShieldBeforeDamagePrevention()
  {
    var state = CreateDamageState(
      preventionAmount: 5,
      keywords: ["DIVINE_SHIELD"]);
    var batch = CreateDamageBatch(state, amount: 3, preventable: true);
    var resolver = new ReplacementResolver();

    var result = resolver.Resolve(new ReplacementResolutionRequest(
      state,
      batch,
      [CreateProposedEvent("event-damage", 1, ProposedEventType.DamageWouldBeDealt)]));

    Assert.True(result.IsAccepted);
    Assert.DoesNotContain(result.FinalBatch!.Mutations, mutation =>
      mutation.MutationType == ApplyDamageMutationHandler.SupportedMutationType);
    Assert.Contains(result.FinalBatch.Mutations, mutation =>
      mutation.MutationType == RemoveKeywordMutationHandler.SupportedMutationType
      && mutation.Metadata?["keyword"] == "DIVINE_SHIELD"
      && mutation.Metadata?["entityInstanceId"] == "entity-1");
    Assert.DoesNotContain(result.FinalBatch.Mutations, mutation =>
      mutation.MutationType == RemoveModifierMutationHandler.SupportedMutationType);
    var decision = Assert.Single(result.Decisions);
    Assert.Equal(ReplacementDecisionResult.Cancelled, decision.Result);
    Assert.Equal("DIVINE_SHIELD_CONSUMED", decision.Reason);
  }

  [Fact]
  public void ResolveLeavesNonPreventableDamageUnchanged()
  {
    var state = CreateDamageState(preventionAmount: 5);
    var batch = CreateDamageBatch(state, amount: 3, preventable: false);
    var resolver = new ReplacementResolver();

    var result = resolver.Resolve(new ReplacementResolutionRequest(
      state,
      batch,
      [CreateProposedEvent("event-damage", 1, ProposedEventType.DamageWouldBeDealt, preventionAllowed: false)]));

    Assert.True(result.IsAccepted);
    Assert.Equal(3, Assert.Single(result.FinalBatch!.Mutations).Payload["amount"]);
    Assert.Equal(ReplacementDecisionResult.Unchanged, Assert.Single(result.Decisions).Result);
  }

  private ProposedMutationBatch CreateBatch(IGameState state) =>
    new(
      "batch-1",
      1,
      null,
      "ON_PLAY",
      "source",
      "TEST",
      [
        new StateMutation(
          "mutation-1",
          IncrementCounterMutationHandler.SupportedMutationType,
          "source",
          "counter:damage",
          new SortedDictionary<string, int>(StringComparer.Ordinal)
          {
            ["amount"] = 1
          },
          "ON_PLAY",
          1,
          null,
          "001")
      ],
      _hasher.HashGameplayState(state),
      null,
      null);

  private ProposedMutationBatch CreateDamageBatch(IGameState state, int amount, bool preventable) =>
    new(
      "batch-1",
      1,
      null,
      "ON_PLAY",
      "source",
      "TEST",
      [
        new StateMutation(
          "mutation-1",
          ApplyDamageMutationHandler.SupportedMutationType,
          "source",
          "entity-1",
          new SortedDictionary<string, int>(StringComparer.Ordinal)
          {
            ["amount"] = amount
          },
          "ON_PLAY",
          1,
          null,
          "001",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["entityInstanceId"] = "entity-1",
            ["playerId"] = "player-1",
            ["preventable"] = preventable.ToString(System.Globalization.CultureInfo.InvariantCulture)
          })
      ],
      _hasher.HashGameplayState(state),
      null,
      null);

  private static InMemoryGameState CreateDamageState(
    int preventionAmount,
    IReadOnlyList<string>? keywords = null) =>
    new(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = new PlayerState(
          "player-1",
          new PlayerZones(
            board:
            [
              new EntityInstance(
                "entity-1",
                "card-1",
                "player-1",
                EntityType.Minion,
                Attack: 1,
                Health: 5,
                Damage: 0,
                Keywords: keywords)
            ]),
          Hero: null,
          Mana: 0,
          MaxMana: 0,
          Armor: 0,
          FatigueCounter: 0,
          Modifiers:
          [
            new PlayerModifier(
              "prevent-1",
              PlayerModifierTypes.DamagePrevention,
              preventionAmount,
              PlayerModifierDurations.NextDamage,
              Priority: 10,
              "source:prevent")
          ])
      });

  private static ProposedEventRecord CreateProposedEvent(
    string eventId,
    long eventSeqCandidate,
    ProposedEventType eventType = ProposedEventType.DamageWouldBeDealt,
    bool preventionAllowed = true) =>
    new(
      eventId,
      eventSeqCandidate,
      1,
      null,
      Phase.OnPlay,
      eventType,
      [new EventRef("SOURCE", "source-1", "BOARD", "player-1")],
      [new EventRef("ENTITY_INSTANCE", "entity-1", "BOARD", "player-1")],
      "mutation-1",
      [new EventPayloadEntry("amount", new EventPayloadValue("INT", null, 1, null, []))],
      "DAMAGE_REPLACEMENT",
      PreventionAllowed: preventionAllowed,
      [],
      EventVisibility.Public);
}

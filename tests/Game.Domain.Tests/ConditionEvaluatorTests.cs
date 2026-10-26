using System.Text.Json;
using Game.Domain.Conditions;
using Game.Domain.Pooling;
using Game.Domain.Targeting;
using Game.Domain.State;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Domain.Tests;

public sealed class ConditionEvaluatorTests
{
  [Fact]
  public void PlayerHealthConditionEvaluatesWithoutMutatingState()
  {
    var state = CreateTwoPlayerState(player1HeroDamage: 18);
    var evaluator = new ConditionEvaluator();
    var request = new ConditionEvaluationRequest(
      [CreateCondition("PLAYER_HEALTH_LESS_OR_EQUAL", new { controller = "SELF", threshold = 12 })],
      "player-1");

    var result = evaluator.Evaluate(state, request);

    Assert.True(result.IsAccepted);
    Assert.True(result.AreAllSatisfied);
    Assert.True(Assert.Single(result.Evaluations).IsSatisfied);
    Assert.Equal(18, state.Players["player-1"].Hero?.Damage);
  }

  [Fact]
  public void BoardAndHandCapacityConditionsPreserveInputOrder()
  {
    var state = CreateTwoPlayerState();
    var evaluator = new ConditionEvaluator();
    var request = new ConditionEvaluationRequest(
      [
        CreateCondition("BOARD_HAS_SPACE", new { controller = "SELF", maxBoardSize = 3 }),
        CreateCondition("HAND_HAS_SPACE", new { controller = "OPPONENT", maxHandSize = 1 })
      ],
      "player-1");

    var result = evaluator.Evaluate(state, request);

    Assert.True(result.IsAccepted);
    Assert.False(result.AreAllSatisfied);
    Assert.Equal(
      ["BOARD_HAS_SPACE", "HAND_HAS_SPACE"],
      result.Evaluations.Select(evaluation => evaluation.ConditionKind));
    Assert.True(result.Evaluations[0].IsSatisfied);
    Assert.False(result.Evaluations[1].IsSatisfied);
  }

  [Fact]
  public void HasValidTargetUsesTargetResolver()
  {
    var state = CreateTwoPlayerState();
    var evaluator = new ConditionEvaluator();
    var request = new ConditionEvaluationRequest(
      [CreateCondition("HAS_VALID_TARGET", new { })],
      "player-1",
      TargetSpec: BoardMinionTargetSpec(
        TargetSelectionMode.AutoValid,
        controller: "OPPONENT"));

    var result = evaluator.Evaluate(state, request);

    Assert.True(result.IsAccepted);
    Assert.True(Assert.Single(result.Evaluations).IsSatisfied);
  }

  [Fact]
  public void HasValidTargetIsFalseWhenTargetResolverRejects()
  {
    var state = CreateTwoPlayerState();
    var evaluator = new ConditionEvaluator();
    var request = new ConditionEvaluationRequest(
      [CreateCondition("HAS_VALID_TARGET", new { })],
      "player-1",
      TargetSpec: BoardMinionTargetSpec(
        TargetSelectionMode.AutoValid,
        controller: "OPPONENT",
        min: 3,
        max: 3));

    var result = evaluator.Evaluate(state, request);

    Assert.True(result.IsAccepted);
    Assert.False(result.AreAllSatisfied);
    Assert.False(Assert.Single(result.Evaluations).IsSatisfied);
  }

  [Fact]
  public void SourceIsInZoneEvaluatesBoardEntity()
  {
    var state = CreateTwoPlayerState();
    var evaluator = new ConditionEvaluator();
    var request = new ConditionEvaluationRequest(
      [CreateCondition("SOURCE_IS_IN_ZONE", new { zones = new[] { "BOARD" } })],
      "player-1",
      Source: new ResolvedTargetRef(
        ResolvedTargetRef.EntityKind,
        "player-1",
        "entity-self-a"));

    var result = evaluator.Evaluate(state, request);

    Assert.True(result.IsAccepted);
    Assert.True(Assert.Single(result.Evaluations).IsSatisfied);
  }

  [Fact]
  public void PoolNotEmptyUsesPoolResolver()
  {
    var state = CreateTwoPlayerState();
    var evaluator = new ConditionEvaluator();
    var pool = FixedPool([new PoolItemRef("CARD", "alpha")]);
    var request = new ConditionEvaluationRequest(
      [CreateCondition("POOL_NOT_EMPTY", new { poolId = "pool-1" })],
      "player-1",
      Pools: new Dictionary<string, FixedPoolDefinition>(StringComparer.Ordinal)
      {
        [pool.PoolId] = pool
      });

    var result = evaluator.Evaluate(state, request);

    Assert.True(result.IsAccepted);
    Assert.True(Assert.Single(result.Evaluations).IsSatisfied);
  }

  [Fact]
  public void PoolNotEmptyRejectsMissingPool()
  {
    var state = CreateTwoPlayerState();
    var evaluator = new ConditionEvaluator();
    var request = new ConditionEvaluationRequest(
      [CreateCondition("POOL_NOT_EMPTY", new { poolId = "pool-1" })],
      "player-1");

    var result = evaluator.Evaluate(state, request);

    Assert.False(result.IsAccepted);
    Assert.Contains(result.Errors, error =>
      error.Code == ConditionEvaluationErrorCode.MissingPool
      && error.ConditionKind == "POOL_NOT_EMPTY");
  }

  [Fact]
  public void PoolNotEmptyRejectsUnresolvablePool()
  {
    var state = CreateTwoPlayerState();
    var evaluator = new ConditionEvaluator();
    var pool = FixedPool([]);
    var request = new ConditionEvaluationRequest(
      [CreateCondition("POOL_NOT_EMPTY", new { poolId = "pool-1" })],
      "player-1",
      Pools: new Dictionary<string, FixedPoolDefinition>(StringComparer.Ordinal)
      {
        [pool.PoolId] = pool
      });

    var result = evaluator.Evaluate(state, request);

    Assert.False(result.IsAccepted);
    Assert.Contains(result.Errors, error =>
      error.Code == ConditionEvaluationErrorCode.PoolResolutionFailed
      && error.ConditionKind == "POOL_NOT_EMPTY");
  }

  [Fact]
  public void MissingParameterRejectsConditionEvaluation()
  {
    var state = CreateTwoPlayerState();
    var evaluator = new ConditionEvaluator();
    var request = new ConditionEvaluationRequest(
      [CreateCondition("PLAYER_HEALTH_LESS_OR_EQUAL", new { controller = "SELF" })],
      "player-1");

    var result = evaluator.Evaluate(state, request);

    Assert.False(result.IsAccepted);
    Assert.Contains(result.Errors, error =>
      error.Code == ConditionEvaluationErrorCode.MissingParameter
      && error.ConditionKind == "PLAYER_HEALTH_LESS_OR_EQUAL");
  }

  [Fact]
  public void UnsupportedConditionKindIsRejected()
  {
    var state = CreateTwoPlayerState();
    var evaluator = new ConditionEvaluator();
    var request = new ConditionEvaluationRequest(
      [CreateCondition("HISTORY_QUERY_NOT_EMPTY", new { historyQuery = "lastDamage" })],
      "player-1");

    var result = evaluator.Evaluate(state, request);

    Assert.False(result.IsAccepted);
    Assert.Contains(result.Errors, error =>
      error.Code == ConditionEvaluationErrorCode.UnsupportedConditionKind);
  }

  private static Condition CreateCondition(string kind, object parameters)
  {
    return new Condition(kind, JsonSerializer.SerializeToElement(parameters));
  }

  private static TargetSpec BoardMinionTargetSpec(
    TargetSelectionMode mode,
    string controller,
    int min = 1,
    int max = 1)
  {
    return new TargetSpec(
      Required: min > 0,
      mode,
      new EntityQuery(
        controller,
        [SourceZone.Board],
        [EntityType.Minion],
        RequiredTags: [],
        ExcludedTags: [],
        AliveOnly: true),
      new CountSpec(min, max),
      "FIZZLE");
  }

  private static FixedPoolDefinition FixedPool(IReadOnlyList<PoolItemRef> items)
  {
    return new FixedPoolDefinition(
      "pool-1",
      PoolResolver.FixedPoolType,
      items);
  }

  private static InMemoryGameState CreateTwoPlayerState(int player1HeroDamage = 0)
  {
    var player1 = PlayerState.Empty("player-1") with
    {
      Hero = CreateHero("hero-player-1", "player-1", player1HeroDamage),
      Zones = new PlayerZones(
        hand: [CreateCard("card-self-hand")],
        board: [CreateMinion("entity-self-a", "player-1"), CreateMinion("entity-self-b", "player-1")])
    };
    var player2 = PlayerState.Empty("player-2") with
    {
      Hero = CreateHero("hero-player-2", "player-2", damage: 0),
      Zones = new PlayerZones(
        hand: [CreateCard("card-opponent-hand")],
        board: [CreateMinion("entity-opponent-a", "player-2"), CreateMinion("entity-opponent-b", "player-2")])
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
      Health: 3,
      Damage: 0);
  }

  private static EntityInstance CreateHero(
    string entityInstanceId,
    string playerId,
    int damage)
  {
    return new EntityInstance(
      entityInstanceId,
      $"card:{entityInstanceId}",
      playerId,
      EntityType.Hero,
      Attack: 0,
      Health: 30,
      damage);
  }

  private static CardInstance CreateCard(string cardInstanceId)
  {
    return new CardInstance(
      cardInstanceId,
      $"card:{cardInstanceId}",
      "1.0.0",
      "player-1",
      CardType.MinionCard);
  }
}

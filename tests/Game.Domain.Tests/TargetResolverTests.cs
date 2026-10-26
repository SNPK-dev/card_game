using Game.Domain.Targeting;
using Game.Domain.State;
using Game.Domain.Randomness;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Domain.Tests;

public sealed class TargetResolverTests
{
  [Fact]
  public void PlayerChosenBoardTargetIsAcceptedWithoutMutatingState()
  {
    var state = CreateTwoPlayerState();
    var resolver = new TargetResolver();
    var request = new TargetResolutionRequest(
      BoardMinionTargetSpec(TargetSelectionMode.PlayerChosen, controller: "OPPONENT"),
      "player-1",
      ChosenTargets:
      [
        new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", "entity-opponent-a")
      ]);

    var result = resolver.Resolve(state, request);

    Assert.True(result.IsAccepted);
    Assert.Equal("entity-opponent-a", Assert.Single(result.Targets).TargetId);
    Assert.Equal(2, state.Players["player-2"].Zones.Board.Count);
    Assert.Equal(0, state.Players["player-2"].Zones.Board.Single(entity =>
      entity.EntityInstanceId == "entity-opponent-a").Damage);
  }

  [Fact]
  public void InvalidChosenTargetIsRejected()
  {
    var state = CreateTwoPlayerState();
    var resolver = new TargetResolver();
    var request = new TargetResolutionRequest(
      BoardMinionTargetSpec(TargetSelectionMode.PlayerChosen, controller: "OPPONENT"),
      "player-1",
      ChosenTargets:
      [
        new ResolvedTargetRef(ResolvedTargetRef.EntityKind, "player-2", "missing")
      ]);

    var result = resolver.Resolve(state, request);

    Assert.False(result.IsAccepted);
    Assert.Contains(result.Errors, error =>
      error.Code == TargetResolutionErrorCode.InvalidChosenTarget
      && error.TargetId == "missing");
  }

  [Fact]
  public void RequiredTargetWithZeroCountIsInvalid()
  {
    var state = CreateTwoPlayerState();
    var resolver = new TargetResolver();
    var targetSpec = BoardMinionTargetSpec(
      TargetSelectionMode.PlayerChosen,
      controller: "OPPONENT",
      min: 0,
      max: 0) with
    {
      Required = true
    };

    var result = resolver.Resolve(state, new TargetResolutionRequest(targetSpec, "player-1"));

    Assert.False(result.IsAccepted);
    Assert.Contains(result.Errors, error =>
      error.Code == TargetResolutionErrorCode.InvalidTargetSpec);
  }

  [Fact]
  public void AllMatchingTargetsAreOrderedDeterministically()
  {
    var state = CreateTwoPlayerState();
    var resolver = new TargetResolver();
    var targetSpec = BoardMinionTargetSpec(
      TargetSelectionMode.AllMatching,
      controller: "ANY",
      min: 1,
      max: 4);

    var result = resolver.Resolve(state, new TargetResolutionRequest(targetSpec, "player-1"));

    Assert.True(result.IsAccepted);
    Assert.Equal(
      ["entity-self-b", "entity-self-c", "entity-opponent-a", "entity-opponent-b"],
      result.Targets.Select(target => target.TargetId));
  }

  [Fact]
  public void AutoValidChoosesFirstCandidateInDeterministicOrder()
  {
    var state = CreateTwoPlayerState();
    var resolver = new TargetResolver();
    var targetSpec = BoardMinionTargetSpec(
      TargetSelectionMode.AutoValid,
      controller: "ANY",
      min: 1,
      max: 1);

    var result = resolver.Resolve(state, new TargetResolutionRequest(targetSpec, "player-1"));

    Assert.True(result.IsAccepted);
    Assert.Equal("entity-self-b", Assert.Single(result.Targets).TargetId);
  }

  [Fact]
  public void HeroCandidatesAreResolvedFromGlobalZone()
  {
    var state = CreateTwoPlayerState();
    var resolver = new TargetResolver();
    var targetSpec = new TargetSpec(
      Required: true,
      TargetSelectionMode.AutoValid,
      new EntityQuery(
        "SELF",
        [SourceZone.Global],
        [EntityType.Hero],
        RequiredTags: [],
        ExcludedTags: [],
        AliveOnly: true),
      new CountSpec(1, 1),
      "FIZZLE");

    var result = resolver.Resolve(state, new TargetResolutionRequest(targetSpec, "player-1"));

    Assert.True(result.IsAccepted);
    var target = Assert.Single(result.Targets);
    Assert.Equal(ResolvedTargetRef.HeroKind, target.TargetKind);
    Assert.Equal("hero-player-1", target.TargetId);
  }

  [Fact]
  public void RandomSelectionRequiresRngRollLoggingContext()
  {
    var state = CreateTwoPlayerState();
    var resolver = new TargetResolver();
    var targetSpec = BoardMinionTargetSpec(TargetSelectionMode.Random, controller: "ANY");

    var result = resolver.Resolve(state, new TargetResolutionRequest(targetSpec, "player-1"));

    Assert.False(result.IsAccepted);
    Assert.Contains(result.Errors, error =>
      error.Code == TargetResolutionErrorCode.UnsupportedSelectionMode);
  }

  [Fact]
  public void RandomSelectionUsesDeterministicCandidateOrderAndLogsRoll()
  {
    var state = CreateTwoPlayerState();
    var resolver = new TargetResolver();
    var targetSpec = BoardMinionTargetSpec(TargetSelectionMode.Random, controller: "ANY");
    var roller = new DeterministicRngRoller(new GameplayRandomStream(123));

    var result = resolver.Resolve(state, new TargetResolutionRequest(
      targetSpec,
      "player-1",
      RngRoller: roller,
      CommandSeq: 7,
      RngRollScope: "effect:random:test"));

    Assert.True(result.IsAccepted);
    var roll = Assert.Single(result.RngRolls);
    Assert.Equal("rng:7:effect:random:test", roll.RngRollId);
    Assert.Equal(RngStreamKind.RandomTarget, roll.Stream);
    Assert.Equal(4, roll.CandidateCount);
    Assert.InRange(roll.SelectedIndex, 0, 3);
    Assert.Equal(result.Candidates[roll.SelectedIndex], Assert.Single(result.Targets));
    Assert.NotEqual(roll.PreRngState, roll.PostRngState);
  }

  [Fact]
  public void TagFilteredQueryIsRejectedUntilRuntimeTagsExist()
  {
    var state = CreateTwoPlayerState();
    var resolver = new TargetResolver();
    var targetSpec = new TargetSpec(
      Required: true,
      TargetSelectionMode.AutoValid,
      new EntityQuery(
        "ANY",
        [SourceZone.Board],
        [EntityType.Minion],
        RequiredTags: ["beast"],
        ExcludedTags: [],
        AliveOnly: true),
      new CountSpec(1, 1),
      "FIZZLE");

    var result = resolver.Resolve(state, new TargetResolutionRequest(targetSpec, "player-1"));

    Assert.False(result.IsAccepted);
    Assert.Contains(result.Errors, error =>
      error.Code == TargetResolutionErrorCode.UnsupportedTagFilter);
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

  private static InMemoryGameState CreateTwoPlayerState()
  {
    var player1 = PlayerState.Empty("player-1") with
    {
      Hero = CreateHero("hero-player-1", "player-1"),
      Zones = new PlayerZones(board:
      [
        CreateMinion("entity-self-c", "player-1"),
        CreateMinion("entity-self-b", "player-1")
      ])
    };
    var player2 = PlayerState.Empty("player-2") with
    {
      Hero = CreateHero("hero-player-2", "player-2"),
      Zones = new PlayerZones(board:
      [
        CreateMinion("entity-opponent-b", "player-2"),
        CreateMinion("entity-opponent-a", "player-2")
      ])
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
}

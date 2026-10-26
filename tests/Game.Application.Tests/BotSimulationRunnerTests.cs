using Game.Application.Simulation;
using Game.Domain.Commands;
using Game.Domain.State;
using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Application.Tests;

public sealed class BotSimulationRunnerTests
{
  private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-06-29T00:00:00Z");

  [Fact]
  public void SimulationAdmitsAndReplaysSpellPlayCardEnvelopeWithoutEntityRef()
  {
    var spell = CreateCardInstance("spell-instance-1", CardType.SpellCard);
    var version = CreateSpellVersion(spell.CardVersion, manaCost: 0);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Mana = 1,
          MaxMana = 1,
          Zones = new PlayerZones(deck: [spell])
        },
        ["player-2"] = PlayerState.Empty("player-2")
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");

    var simulation = new BotSimulationRunner().Run(new BotSimulationRequest(
      "spell-bot-simulation",
      initialState,
      [version],
      new Dictionary<string, IBotAgent>(StringComparer.Ordinal)
      {
        ["player-1"] = new OraclePriorityBot()
      },
      new BotSimulationOptions(OpeningHandSize: 1, MaxCommands: 1, BotSeed: 29)));

    var payload = Assert.Single(simulation.CommandLog.Payloads);
    Assert.Equal(PlayCardCommand.CommandKind, payload.CommandType);
    Assert.Equal(spell.CardInstanceId, payload.Refs["cardInstanceId"]);
    Assert.False(payload.Refs.ContainsKey("entityInstanceId"));
    Assert.Equal(0, payload.Payload["attack"]);
    Assert.Equal(0, payload.Payload["health"]);
    Assert.Equal(0, payload.Payload["manaCost"]);
    Assert.Empty(simulation.FinalState.Players["player-1"].Zones.Hand);
    Assert.Empty(simulation.FinalState.Players["player-1"].Zones.Board);
    Assert.Equal(spell.CardInstanceId, Assert.Single(simulation.FinalState.Players["player-1"].Zones.Graveyard).CardInstanceId);
    Assert.True(simulation.ReplayVerification.IsVerified);
  }

  private static CardInstance CreateCardInstance(
    string cardInstanceId,
    CardType cardType) =>
    new(
      cardInstanceId,
      "card:arcane-spark",
      "card_arcane_spark_v1",
      "player-1",
      cardType);

  private static CardVersion CreateSpellVersion(string cardVersionId, int manaCost)
  {
    var definition = new CardDefinition(
      "card:arcane-spark",
      cardVersionId,
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      "primitive-registry-bootstrap-0.2.0",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData(
        "Arcane Spark",
        CardType.SpellCard,
        ["mage"],
        Rarity.Common,
        manaCost,
        [],
        [],
        Collectible: true,
        ["mvp"]),
      new EntityDefinition(
        Minion: null,
        new SpellDefinition(null, RequiresTarget: false),
        Weapon: null,
        Location: null,
        HeroCard: null,
        HeroPower: null),
      [],
      new CardTextGeneration("Simple spell.", FlavorText: null),
      new BalanceMetadata(1, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("test", FixedNow, "manual-fixture", []));

    return new CardVersion(
      "card:arcane-spark",
      cardVersionId,
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:resolved",
      FixedNow,
      definition);
  }
}

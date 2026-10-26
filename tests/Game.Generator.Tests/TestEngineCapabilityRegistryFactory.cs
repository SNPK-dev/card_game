using Game.Shared.Engine;

namespace Game.Generator.Tests;

internal static class TestEngineCapabilityRegistryFactory
{
  public static EngineCapabilityRegistry Create() =>
    new(
      "engine-bootstrap-0.3.0",
      ["card-dsl-0.2.0", "dsl-v1"],
      ["primitive-registry-bootstrap-0.2.0", "primitive-v1"],
      ["ruleset_mvp_core_v1", "0.2.0"],
      [
        "BATCH_DESTROY",
        "DEAL_DAMAGE",
        "DESTROY",
        "DISCARD_CARD",
        "DRAW_CARD",
        "FULLY_HEAL",
        "HEAL",
        "MOVE_TO_ZONE",
        "PLAY_CARD",
        "SHUFFLE_INTO_DECK",
        "SUMMON"
      ],
      ["SIMPLE_PLAY_EFFECT"],
      [
        "BATTLECRY",
        "DETERMINISTIC_RNG_ROLL_LOGGING",
        "MUTATION_BATCH_PIPELINE",
        "RANDOM_TARGET_RESOLUTION",
        "VANILLA_MINION"
      ],
      ["ON_PLAY", "ON_SUMMON", "TURN_START", "TURN_END"],
      ["BOARD", "DECK", "GRAVEYARD", "HAND", "SET_ASIDE"],
      ["AUTO_VALID", "FIXED_POOL_ORDER", "SEQUENTIAL", "SOURCE_ORDER"],
      ["FATIGUE", "FIZZLE", "FIZZLE_WHOLE_EFFECT", "SKIP"],
      ["asset-manifest-bootstrap-0.1.0"],
      new EngineCapabilityMetadata(
        "test",
        DateTimeOffset.Parse("2026-06-26T00:00:00Z"),
        "test",
        DateTimeOffset.Parse("2026-06-26T00:00:00Z"),
        ["tests/Game.Generator.Tests/TestEngineCapabilityRegistryFactory.cs"],
        ["Generator publication tests use an application-independent registry fixture."]));
}

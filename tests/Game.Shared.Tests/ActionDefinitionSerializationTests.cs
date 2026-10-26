using System.Text.Json;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Shared.Tests;

public sealed class ActionDefinitionSerializationTests
{
  [Fact]
  public void TierZeroActionUnionSerializesWithStableDiscriminators()
  {
    ActionDefinition[] actions =
    [
      new DamageDistributionActionDefinition(
        "action-distribute-damage",
        Fixed(5),
        new EntityQuery("ANY", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
        "RANDOM_SPLIT",
        "SEPARATE_DAMAGE_INSTANCES",
        "AGGREGATE_LIFESTEAL",
        "ALLOW_OVERKILL",
        FizzleIfNoTargets: true,
        "RANDOM_TARGET"),
      new HealActionDefinition("action-heal", Fixed(2), new TargetRef("TARGET")),
      new FullyHealActionDefinition("action-fully-heal", new TargetRef("TARGET")),
      new DiscardCardActionDefinition("action-discard", new TargetRef("TARGET"), SourceZone.Hand, "FIZZLE_WHOLE_EFFECT"),
      new DestroyActionDefinition("action-destroy", new TargetRef("TARGET"), "FIZZLE_WHOLE_EFFECT"),
      new BatchDestroyActionDefinition("action-batch-destroy", new TargetRef("TARGETS"), "SIMULTANEOUS", "SKIP_INVALID_TARGETS"),
      new RandomSelectActionDefinition(
        "action-random",
        new EntityQuery("ANY", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
        new CountSpec(1, 1),
        "survivor",
        "RANDOM_TARGET",
        "SKIP_ACTION"),
      new MoveToZoneActionDefinition("action-move", new TargetRef("TARGET"), SourceZone.Graveyard, "KEEP_OWNER", "PUBLIC", null),
      new ReturnToHandActionDefinition("action-return", new TargetRef("TARGET"), "ORIGINAL_OWNER", "SKIP_ACTION", null),
      new ApplyModifierActionDefinition(
        "action-modifier",
        PlayerSelector.Self,
        new ModifierDefinition(
          "modifier-cost-next",
          "COST_MODIFIER",
          Fixed(2),
          "NEXT_PLAY",
          Priority: 0)),
      new ApplyAuraActionDefinition(
        "action-aura",
        PlayerSelector.Self,
        new AuraDefinition(
          "aura-board-attack",
          "BOARD_ATTACK_AURA",
          new EntityQuery("OWN", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
          new ModifierDefinition(
            "modifier-aura-attack",
            "BOARD_ATTACK_MODIFIER",
            Fixed(1),
            "WHILE_SOURCE_ACTIVE",
            Priority: 0),
          "WHILE_SOURCE_ACTIVE",
          Priority: 0)),
      new PreventDamageActionDefinition(
        "action-prevent",
        new TargetRef("TARGET"),
        Fixed(3),
        "NEXT_DAMAGE",
        "PREVENT_UP_TO_AMOUNT",
        ResultRef: "prevented-damage"),
      new ReplaceEventActionDefinition(
        "action-replace",
        "APPLY_DAMAGE",
        new TargetRef("TARGET"),
        "REPLACE_WITH_HEAL",
        "NEXT_EVENT",
        ResultRef: "replacement-event"),
      new ShuffleIntoDeckActionDefinition("action-shuffle", new TargetRef("TARGET"), "DETERMINISTIC_APPEND", null),
      new DiscoverActionDefinition(
        "action-discover",
        PlayerSelector.Self,
        new PoolRef("pool-spells"),
        OfferCount: 3,
        "PLAYER_CHOICE",
        SourceZone.Hand,
        "SKIP_ACTION",
        ResultRef: "choice-spell"),
      new ResurrectActionDefinition(
        "action-resurrect",
        new TargetRef("TARGET"),
        PlayerSelector.Self,
        Fixed(2),
        Fixed(3),
        "APPEND",
        "SKIP_ACTION",
        ResultRef: "resurrected-minion",
        new HistoryQuerySpec(
          "OWN_GRAVEYARD_MINION",
          PlayerSelector.Self,
          "LAST",
          MaxCandidates: 10)),
      new TakeExtraTurnActionDefinition(
        "action-extra-turn",
        PlayerSelector.Self,
        OncePerGame: true,
        CurrentTurnEndsNormally: true,
        "AFTER_CURRENT_TURN",
        "QUEUE_AFTER_CURRENT_TURN",
        CommandButtonReplacement: null,
        "EXPIRE_WITH_ORIGINAL_TURN")
    ];

    var json = JsonSerializer.Serialize(actions);
    var roundTripped = JsonSerializer.Deserialize<ActionDefinition[]>(json);

    Assert.Contains("\"actionType\":\"DAMAGE_DISTRIBUTION\"", json, StringComparison.Ordinal);
    Assert.Contains("\"actionType\":\"HEAL\"", json, StringComparison.Ordinal);
    Assert.Contains("\"actionType\":\"FULLY_HEAL\"", json, StringComparison.Ordinal);
    Assert.Contains("\"actionType\":\"DISCARD_CARD\"", json, StringComparison.Ordinal);
    Assert.Contains("\"actionType\":\"DESTROY\"", json, StringComparison.Ordinal);
    Assert.Contains("\"actionType\":\"BATCH_DESTROY\"", json, StringComparison.Ordinal);
    Assert.Contains("\"actionType\":\"RANDOM_SELECT\"", json, StringComparison.Ordinal);
    Assert.Contains("\"actionType\":\"MOVE_TO_ZONE\"", json, StringComparison.Ordinal);
    Assert.Contains("\"actionType\":\"RETURN_TO_HAND\"", json, StringComparison.Ordinal);
    Assert.Contains("\"actionType\":\"APPLY_MODIFIER\"", json, StringComparison.Ordinal);
    Assert.Contains("\"actionType\":\"APPLY_AURA\"", json, StringComparison.Ordinal);
    Assert.Contains("\"actionType\":\"PREVENT_DAMAGE\"", json, StringComparison.Ordinal);
    Assert.Contains("\"actionType\":\"REPLACE_EVENT\"", json, StringComparison.Ordinal);
    Assert.Contains("\"actionType\":\"SHUFFLE_INTO_DECK\"", json, StringComparison.Ordinal);
    Assert.Contains("\"actionType\":\"DISCOVER\"", json, StringComparison.Ordinal);
    Assert.Contains("\"actionType\":\"RESURRECT\"", json, StringComparison.Ordinal);
    Assert.Contains("\"actionType\":\"TAKE_EXTRA_TURN\"", json, StringComparison.Ordinal);
    Assert.Contains("\"HistoryQuery\":{\"Kind\":\"OWN_GRAVEYARD_MINION\"", json, StringComparison.Ordinal);
    Assert.Collection(
      roundTripped!,
      action => Assert.IsType<DamageDistributionActionDefinition>(action),
      action => Assert.IsType<HealActionDefinition>(action),
      action => Assert.IsType<FullyHealActionDefinition>(action),
      action => Assert.IsType<DiscardCardActionDefinition>(action),
      action => Assert.IsType<DestroyActionDefinition>(action),
      action => Assert.IsType<BatchDestroyActionDefinition>(action),
      action => Assert.IsType<RandomSelectActionDefinition>(action),
      action => Assert.IsType<MoveToZoneActionDefinition>(action),
      action => Assert.IsType<ReturnToHandActionDefinition>(action),
      action => Assert.IsType<ApplyModifierActionDefinition>(action),
      action => Assert.IsType<ApplyAuraActionDefinition>(action),
      action => Assert.IsType<PreventDamageActionDefinition>(action),
      action => Assert.IsType<ReplaceEventActionDefinition>(action),
      action => Assert.IsType<ShuffleIntoDeckActionDefinition>(action),
      action => Assert.IsType<DiscoverActionDefinition>(action),
      action => Assert.IsType<ResurrectActionDefinition>(action),
      action => Assert.IsType<TakeExtraTurnActionDefinition>(action));
  }

  private static ValueExpression Fixed(int value) => new("FIXED", value, null);
}

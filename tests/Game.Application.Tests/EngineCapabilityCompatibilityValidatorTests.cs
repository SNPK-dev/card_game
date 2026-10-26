using Game.Application.Capabilities;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Game.Shared.Ruleset;
using Xunit;

namespace Game.Application.Tests;

public sealed class EngineCapabilityCompatibilityValidatorTests
{
  [Fact]
  public void CompatibilityValidatorAcceptsSupportedRulesetAndCard()
  {
    var registry = new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry();
    var validator = new EngineCapabilityCompatibilityValidator();

    var result = validator.Validate(new EngineCapabilityCompatibilityRequest(
      registry,
      CreateSupportedRulesetProfile(),
      [CreateSupportedCard()]));

    Assert.True(result.IsCompatible);
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public void CompatibilityValidatorRejectsUnsupportedRulesetRequirement()
  {
    var registry = new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry();
    var profile = CreateSupportedRulesetProfile() with
    {
      AllowedActions = ["DEAL_DAMAGE", "FUTURE_ACTION"]
    };
    var validator = new EngineCapabilityCompatibilityValidator();

    var result = validator.Validate(new EngineCapabilityCompatibilityRequest(
      registry,
      profile,
      []));

    Assert.False(result.IsCompatible);
    var diagnostic = Assert.Single(result.Diagnostics);
    Assert.Equal("UNSUPPORTED_ACTION", diagnostic.Code);
    Assert.Equal("rulesetProfile.allowedActions", diagnostic.Path);
    Assert.Equal("FUTURE_ACTION", diagnostic.Identifier);
  }

  [Fact]
  public void CompatibilityValidatorAcceptsImplementedTierZeroEffectActions()
  {
    var registry = new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry();
    var validator = new EngineCapabilityCompatibilityValidator();
    var card = CreateSupportedCard() with
    {
      Effects =
      [
        CreateSupportedEffect() with
        {
          Actions =
          [
            new HealActionDefinition(
              "action_heal_001",
              new ValueExpression("FIXED", 2, null),
              new TargetRef("targetRequirement")),
            new FullyHealActionDefinition(
              "action_fully_heal_001",
              new TargetRef("targetRequirement")),
            new DestroyActionDefinition(
              "action_destroy_001",
              new TargetRef("targetRequirement"),
              "FIZZLE_WHOLE_EFFECT"),
            new BatchDestroyActionDefinition(
              "action_batch_destroy_001",
              new TargetRef("targetRequirement"),
              "SIMULTANEOUS",
              "SKIP_INVALID_TARGETS"),
            new RandomSelectActionDefinition(
              "action_random_select_001",
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
              "SKIP_ACTION"),
            new DiscardCardActionDefinition(
              "action_discard_001",
              new TargetRef("targetRequirement"),
              SourceZone.Hand,
              "FIZZLE_WHOLE_EFFECT"),
            new MoveToZoneActionDefinition(
              "action_move_001",
              new TargetRef("targetRequirement"),
              SourceZone.Graveyard,
              "KEEP_OWNER",
              "PUBLIC",
              null),
            new ReturnToHandActionDefinition(
              "action_return_001",
              new TargetRef("targetRequirement"),
              "ORIGINAL_OWNER",
              "SKIP_ACTION",
              null),
            new ApplyModifierActionDefinition(
              "action_apply_modifier_001",
              PlayerSelector.Self,
              new ModifierDefinition(
                "modifier_next_play_001",
                "COST_MODIFIER",
                new ValueExpression("FIXED", 1, null),
                "NEXT_PLAY",
                Priority: 0)),
            new DiscoverActionDefinition(
              "action_discover_001",
              PlayerSelector.Self,
              new PoolRef("pool-spells"),
              OfferCount: 3,
              "PLAYER_CHOICE",
              SourceZone.Hand,
              "SKIP_ACTION",
              ResultRef: "choice_spell"),
            new ResurrectActionDefinition(
              "action_resurrect_001",
              new TargetRef("targetRequirement"),
              PlayerSelector.Self,
              new ValueExpression("FIXED", 2, null),
              new ValueExpression("FIXED", 3, null),
              "APPEND",
              "SKIP_ACTION",
              ResultRef: "resurrected_minion"),
            new ShuffleIntoDeckActionDefinition(
              "action_shuffle_001",
              new TargetRef("targetRequirement"),
              "DETERMINISTIC_APPEND",
              null)
          ]
        }
      ]
    };

    var result = validator.Validate(new EngineCapabilityCompatibilityRequest(
      registry,
      null,
      [card]));

    Assert.True(result.IsCompatible);
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public void CompatibilityValidatorRejectsUnsupportedCardActionBeforeGameplay()
  {
    var registry = new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry();
    var validator = new EngineCapabilityCompatibilityValidator();
    var card = CreateSupportedCard() with
    {
      Effects =
      [
        CreateSupportedEffect() with
        {
          Actions =
          [
            new AddCardToHandActionDefinition(
              "action_add_card_001",
              new CardSource("CARD_REF", "card:token"),
              PlayerSelector.Self,
              "FIZZLE_IF_FULL",
              null)
          ]
        }
      ]
    };

    var result = validator.Validate(new EngineCapabilityCompatibilityRequest(
      registry,
      null,
      [card]));

    Assert.False(result.IsCompatible);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "UNSUPPORTED_ACTION"
      && diagnostic.Path == "cardDefinitions[0].effects[0].actions[0]"
      && diagnostic.Identifier == "ADD_CARD_TO_HAND");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "CARD_REJECTED_BY_CAPABILITY_REGISTRY"
      && diagnostic.Identifier == "card_arcane_apprentice");
  }

  [Fact]
  public void CompatibilityValidatorAcceptsSupportedExtraTurnAction()
  {
    var registry = new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry();
    var validator = new EngineCapabilityCompatibilityValidator();
    var card = CreateSupportedCard() with
    {
      Effects =
      [
        CreateSupportedEffect() with
        {
          Actions =
          [
            new TakeExtraTurnActionDefinition(
              "action_extra_turn_001",
              PlayerSelector.Self,
              OncePerGame: true,
              CurrentTurnEndsNormally: true,
              "AFTER_CURRENT_TURN",
              "QUEUE_AFTER_CURRENT_TURN",
              null,
              "EXPIRE_CURRENT_TURN_ONLY")
          ]
        }
      ]
    };

    var result = validator.Validate(new EngineCapabilityCompatibilityRequest(
      registry,
      null,
      [card]));

    Assert.True(result.IsCompatible);
    Assert.Empty(result.Diagnostics);
  }

  private static CardDefinition CreateSupportedCard()
  {
    return new CardDefinition(
      "card_arcane_apprentice",
      "1.0.0",
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      "primitive-registry-bootstrap-0.2.0",
      "ruleset_mvp_core_v1",
      "catalog_alpha",
      new StaticCardData(
        "Arcane Apprentice",
        CardType.MinionCard,
        ["mage"],
        Rarity.Rare,
        2,
        ["APPRENTICE"],
        ["BATTLECRY"],
        Collectible: true,
        ["mvp"]),
      new EntityDefinition(
        new MinionDefinition(
          2,
          2,
          ["APPRENTICE"],
          [],
          CanAttack: true),
        null,
        null,
        null,
        null,
        null),
      [CreateSupportedEffect()],
      new CardTextGeneration(
        "Battlecry: Deal 3 damage.",
        null),
      new BalanceMetadata(
        8,
        0,
        []),
      new AssetMetadata(
        null,
        null,
        null),
      new AuthoringMetadata(
        "compatibility-test",
        DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
        "manual-fixture",
        ["openspec/changes/archive/2026-06-24-engine-capability-runtime-bootstrap/design.md"]));
  }

  private static EffectDefinition CreateSupportedEffect()
  {
    return new EffectDefinition(
      "effect_battlecry_damage",
      EffectKind.SimplePlayEffect,
      [SourceZone.Hand],
      null,
      new TimingPolicy(
        Phase.OnPlay,
        null,
        0,
        "SOURCE_ORDER"),
      [],
      new TargetSpec(
        Required: true,
        TargetSelectionMode.PlayerChosen,
        new EntityQuery(
          "ANY",
          [SourceZone.Board],
          [EntityType.Minion],
          [],
          [],
          AliveOnly: true),
        new CountSpec(1, 1),
        "FIZZLE_IF_INVALID"),
      [
        new DealDamageActionDefinition(
          "action_damage_001",
          new ValueExpression("FIXED", 3, null),
          new TargetRef("targetRequirement"),
          "ARCANE",
          Lifesteal: false,
          Preventable: true)
      ],
      new ResolutionPolicy("AUTO_VALID"),
      new FizzlePolicy("FIZZLE_WHOLE_EFFECT"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      ["BATTLECRY"]);
  }

  private static ResolvedRulesetProfile CreateSupportedRulesetProfile()
  {
    return new ResolvedRulesetProfile(
      "resolved_mvp_core_v1",
      "ruleset_mvp_core_v1",
      "0.2.0",
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      "profile-resolver",
      "sha256:example",
      "mvp_internal",
      "core_generated_alpha",
      ["BATTLECRY", "MUTATION_BATCH_PIPELINE"],
      [],
      [new FormatMechanicState("BATTLECRY", "ruleset_mvp_core_v1", FormatMechanicStateKind.Enabled, null, null, null)],
      ["SIMPLE_PLAY_EFFECT"],
      ["DEAL_DAMAGE", "DRAW_CARD", "PLAY_CARD", "SUMMON"],
      [],
      ["BOARD", "HAND"],
      ["ON_PLAY", "ON_SUMMON"],
      [],
      ["MINION_CARD"],
      ["MINION"],
      [],
      [],
      [],
      CreateLimits(),
      CreateValidationPolicy(),
      CreateBalancePolicy(),
      CreateSimulationPolicy(),
      CreatePublicationPolicy(),
      CreateLegacyPolicy());
  }

  private static RulesetLimits CreateLimits()
  {
    return new RulesetLimits(
      30,
      25,
      20,
      80,
      5,
      1,
      "LIMITED",
      3,
      2,
      3,
      3,
      3,
      2,
      2,
      3,
      1);
  }

  private static ValidationPolicyRef CreateValidationPolicy()
  {
    return new ValidationPolicyRef(
      "validation_mvp",
      true,
      true,
      true,
      true,
      true,
      true,
      true,
      true,
      true);
  }

  private static BalancePolicyRef CreateBalancePolicy()
  {
    return new BalancePolicyRef(
      "balance_mvp",
      true,
      "MEDIUM",
      [],
      0.05,
      30,
      25,
      5,
      100,
      0.95,
      "baseline_mvp",
      "seed_mvp",
      "flag_outliers");
  }

  private static SimulationPolicyRef CreateSimulationPolicy()
  {
    return new SimulationPolicyRef(
      "simulation_mvp",
      ["smoke"],
      ["empty_deck"],
      10,
      10,
      true,
      30,
      80);
  }

  private static PublicationPolicyRef CreatePublicationPolicy()
  {
    return new PublicationPolicyRef(
      "publication_mvp",
      true,
      true,
      true,
      true,
      true,
      false,
      false,
      ["validated"]);
  }

  private static LegacyPolicy CreateLegacyPolicy()
  {
    return new LegacyPolicy(
      true,
      false,
      true,
      false,
      true);
  }
}

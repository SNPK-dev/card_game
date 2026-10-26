using Game.Generator.Generation;
using Game.Generator.Validation;
using Game.Shared.Dsl;
using Game.Shared.Mechanics;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Generator.Tests;

public sealed class GeneratedCardCandidateValidatorTests
{
  [Fact]
  public void ValidateAcceptsCandidateInsideGenerationContext()
  {
    var context = CreateContext();
    var candidate = TestFixtures.CreateBattlecryCandidate();

    var result = new GeneratedCardCandidateValidator().Validate(candidate, context);

    Assert.True(result.IsValid);
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public void ValidateRejectsImmatureMechanicTag()
  {
    var profile = TestFixtures.CreateProfile(enabledMechanics: ["BATTLECRY", "SMART_OFFER"]);
    var context = new GenerationContextBuilder().Build(
      profile,
      [
        TestFixtures.Mechanic("BATTLECRY", MechanicStatus.ApprovedForRuleset),
        TestFixtures.Mechanic("SMART_OFFER", MechanicStatus.SpecDrafted)
      ]);
    var candidate = TestFixtures.CreateBattlecryCandidate(effectTags: ["SMART_OFFER"]);

    var result = new GeneratedCardCandidateValidator().Validate(candidate, context);

    Assert.False(result.IsValid);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.MechanicNotAvailable
      && diagnostic.ActualValue == "SMART_OFFER");
  }

  [Fact]
  public void ValidateRejectsDisallowedProfilePrimitives()
  {
    var context = CreateContext(allowedEffectKinds: ["SIMPLE_PLAY_EFFECT"]);
    var candidate = TestFixtures.CreateBattlecryCandidate(effectKind: EffectKind.BatchEffect);

    var result = new GeneratedCardCandidateValidator().Validate(candidate, context);

    Assert.False(result.IsValid);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.EffectKindNotAllowed
      && diagnostic.ActualValue == "BATCH_EFFECT");
  }

  [Fact]
  public void ValidateRejectsCandidateThatExceedsProfileLimits()
  {
    var context = CreateContext(maxActionsPerEffect: 1, maxEffectsPerCard: 1);
    var candidate = TestFixtures.CreateBattlecryCandidate(
      extraAction: new DrawCardActionDefinition(
        "draw_001",
        PlayerSelector.Self,
        new ValueExpression("FIXED", 1, null),
        "SKIP_ACTION",
        null));

    var result = new GeneratedCardCandidateValidator().Validate(candidate, context);

    Assert.False(result.IsValid);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.ActionsPerEffectLimitExceeded);
  }

  [Fact]
  public void ValidateRejectsDisallowedActionType()
  {
    var context = CreateContext(allowedActions: ["DEAL_DAMAGE"]);
    var candidate = TestFixtures.CreateBattlecryCandidate(
      extraAction: new DrawCardActionDefinition(
        "draw_001",
        PlayerSelector.Self,
        new ValueExpression("FIXED", 1, null),
        "SKIP_ACTION",
        null));

    var result = new GeneratedCardCandidateValidator().Validate(candidate, context);

    Assert.False(result.IsValid);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.ActionTypeNotAllowed
      && diagnostic.ActualValue == "DRAW_CARD");
  }

  [Fact]
  public void ValidateMapsApplyModifierActionToProfileToken()
  {
    var context = CreateContext(allowedActions: ["DEAL_DAMAGE", "APPLY_MODIFIER"]);
    var candidate = TestFixtures.CreateBattlecryCandidate(
      extraAction: new ApplyModifierActionDefinition(
        "modifier_001",
        PlayerSelector.Self,
        new ModifierDefinition(
          "modifier_next_play_001",
          "COST_MODIFIER",
          new ValueExpression("FIXED", 1, null),
          "NEXT_PLAY",
          Priority: 0)));

    var result = new GeneratedCardCandidateValidator().Validate(candidate, context);

    Assert.True(result.IsValid);
  }

  [Fact]
  public void ValidateMapsDiscoverActionToProfileToken()
  {
    var context = CreateContext(allowedActions: ["DEAL_DAMAGE", "DISCOVER"]);
    var candidate = TestFixtures.CreateBattlecryCandidate(
      extraAction: new DiscoverActionDefinition(
        "discover_001",
        PlayerSelector.Self,
        new PoolRef("pool-spells"),
        OfferCount: 3,
        "PLAYER_CHOICE",
        SourceZone.Hand,
        "SKIP_ACTION",
        ResultRef: "choice_spell"));

    var result = new GeneratedCardCandidateValidator().Validate(candidate, context);

    Assert.True(result.IsValid);
  }

  [Fact]
  public void SemanticValidationRejectsDiscoverUntilMatchPoolContextIsRuntimeSafe()
  {
    var context = CreateContext(allowedActions: ["DEAL_DAMAGE", "DISCOVER"]);
    var candidate = TestFixtures.CreateBattlecryCandidate(
      extraAction: new DiscoverActionDefinition(
        "discover_match_pool_unsupported",
        PlayerSelector.Self,
        new PoolRef("UNITY_FIXED_POOL"),
        OfferCount: 3,
        "PLAYER_CHOICE",
        SourceZone.Hand,
        "FIZZLE",
        ResultRef: "choice_spell")) with
    {
      CardText = new CardTextGeneration("Discover a card.", null)
    };

    var result = new SemanticValidationStage().Validate(new GeneratedCardValidationRequest(
      "generation-discover-policy-parity",
      "pipeline-v1",
      "context-v1",
      candidate,
      context));

    Assert.Equal(GeneratedCardValidationDecision.Rejected, result.Decision);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.RuntimeActionNotSupported
      && diagnostic.ActualValue == "DISCOVER");
  }

  [Fact]
  public void SemanticValidationRejectsDrawCountUnsupportedByRuntime()
  {
    var context = CreateContext(allowedActions: ["DEAL_DAMAGE", "DRAW_CARD"]);
    var candidate = TestFixtures.CreateBattlecryCandidate(
      extraAction: new DrawCardActionDefinition(
        "draw_two_unsupported",
        PlayerSelector.Self,
        new ValueExpression("FIXED", 2, null),
        "FATIGUE",
        ResultRef: "drawn_cards")) with
    {
      CardText = new CardTextGeneration("Draw 2 cards.", null)
    };

    var result = new SemanticValidationStage().Validate(new GeneratedCardValidationRequest(
      "generation-draw-count-parity",
      "pipeline-v1",
      "context-v1",
      candidate,
      context));

    Assert.Equal(GeneratedCardValidationDecision.Rejected, result.Decision);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.RuntimeActionNotSupported
      && diagnostic.Path.EndsWith(".count", StringComparison.Ordinal)
      && diagnostic.ActualValue == "2");
  }

  [Fact]
  public void SemanticValidationRejectsUnsupportedTargetReference()
  {
    var context = CreateContext(allowedActions: ["DEAL_DAMAGE", "HEAL"]);
    var unsupportedHeal = new HealActionDefinition(
      "heal_unsupported_target_ref",
      new ValueExpression("FIXED", 2, null),
      new TargetRef("sourceControllerMinions"));
    var candidate = TestFixtures.CreateBattlecryCandidate(
      extraAction: unsupportedHeal) with
    {
      Effects =
      [
        TestFixtures.CreateBattlecryCandidate().Effects[0] with
        {
          TargetRequirement = null,
          Actions = [unsupportedHeal]
        }
      ],
      CardText = new CardTextGeneration("Restore 2 Health to a friendly minion.", null)
    };

    var result = new SemanticValidationStage().Validate(new GeneratedCardValidationRequest(
      "generation-target-ref-parity",
      "pipeline-v1",
      "context-v1",
      candidate,
      context));

    Assert.Equal(GeneratedCardValidationDecision.Rejected, result.Decision);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.RuntimeActionNotSupported
      && diagnostic.Path.EndsWith(".target.ref", StringComparison.Ordinal)
      && diagnostic.ActualValue == "sourceControllerMinions");
  }

  [Fact]
  public void ValidateMapsResurrectActionToProfileToken()
  {
    var context = CreateContext(allowedActions: ["DEAL_DAMAGE", "RESURRECT"]);
    var candidate = TestFixtures.CreateBattlecryCandidate(
      extraAction: new ResurrectActionDefinition(
        "resurrect_001",
        new TargetRef("TARGET"),
        PlayerSelector.Self,
        new ValueExpression("FIXED", 2, null),
        new ValueExpression("FIXED", 3, null),
        "APPEND",
        "SKIP_ACTION",
        ResultRef: "resurrected_minion"));

    var result = new GeneratedCardCandidateValidator().Validate(candidate, context);

    Assert.True(result.IsValid);
  }

  [Theory]
  [MemberData(nameof(ProductionMvpActionTokens))]
  public void ValidateMapsProductionMvpActionToProfileToken(string token, ActionDefinition action)
  {
    var context = CreateContext(allowedActions: ["DEAL_DAMAGE", token]);
    var candidate = TestFixtures.CreateBattlecryCandidate(extraAction: action);

    var result = new GeneratedCardCandidateValidator().Validate(candidate, context);

    Assert.True(result.IsValid);
  }

  public static IEnumerable<object[]> ProductionMvpActionTokens()
  {
    yield return
    [
      "HEAL",
      new HealActionDefinition("heal_001", new ValueExpression("FIXED", 2, null), new TargetRef("targetRequirement"))
    ];
    yield return
    [
      "FULLY_HEAL",
      new FullyHealActionDefinition("fully_heal_001", new TargetRef("targetRequirement"))
    ];
    yield return
    [
      "DISCARD_CARD",
      new DiscardCardActionDefinition("discard_001", new TargetRef("targetRequirement"), SourceZone.Hand, "FIZZLE_WHOLE_EFFECT")
    ];
    yield return
    [
      "DESTROY",
      new DestroyActionDefinition("destroy_001", new TargetRef("targetRequirement"), "FIZZLE_WHOLE_EFFECT")
    ];
    yield return
    [
      "BATCH_DESTROY",
      new BatchDestroyActionDefinition("batch_destroy_001", new TargetRef("targetRequirement"), "SIMULTANEOUS", "SKIP_INVALID_TARGETS")
    ];
    yield return
    [
      "MOVE_TO_ZONE",
      new MoveToZoneActionDefinition("move_001", new TargetRef("targetRequirement"), SourceZone.Graveyard, "KEEP_OWNER", "PUBLIC", null)
    ];
    yield return
    [
      "SHUFFLE_INTO_DECK",
      new ShuffleIntoDeckActionDefinition("shuffle_001", new TargetRef("targetRequirement"), "DETERMINISTIC_APPEND", null)
    ];
    yield return
    [
      "DAMAGE_DISTRIBUTION",
      new DamageDistributionActionDefinition(
        "damage_distribution_001",
        new ValueExpression("FIXED", 5, null),
        new EntityQuery("ANY", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
        "RANDOM_SPLIT",
        "SEPARATE_DAMAGE_INSTANCES",
        "NO_LIFESTEAL",
        "ALLOW_OVERKILL",
        FizzleIfNoTargets: true,
        "RANDOM_TARGET")
    ];
    yield return
    [
      "PREVENT_DAMAGE",
      new PreventDamageActionDefinition(
        "prevent_001",
        new TargetRef("targetRequirement"),
        new ValueExpression("FIXED", 3, null),
        "NEXT_DAMAGE",
        "PREVENT_UP_TO_AMOUNT",
        "prevented_damage")
    ];
    yield return
    [
      "TAKE_EXTRA_TURN",
      new TakeExtraTurnActionDefinition(
        "extra_turn_001",
        PlayerSelector.Self,
        OncePerGame: true,
        CurrentTurnEndsNormally: true,
        "AFTER_CURRENT_TURN",
        "QUEUE_AFTER_CURRENT_TURN",
        null,
        "EXPIRE_CURRENT_TURN_ONLY")
    ];
  }

  [Theory]
  [MemberData(nameof(UnsupportedRuntimeActionCandidates))]
  public void ValidateRejectsActionThatCurrentRuntimeCannotPlay(ActionDefinition action)
  {
    var candidate = TestFixtures.CreateBattlecryCandidate(extraAction: action);

    var result = new SemanticValidationStage().Validate(new GeneratedCardValidationRequest(
      "generation-unsupported-runtime-action",
      "pipeline-v1",
      "context-v1",
      candidate,
      CreateContext()));

    Assert.Equal(GeneratedCardValidationDecision.Rejected, result.Decision);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code is GeneratedCardValidationCode.RuntimeActionNotSupported or GeneratedCardValidationCode.TakeExtraTurnNotSupported);
  }

  public static IEnumerable<object[]> UnsupportedRuntimeActionCandidates()
  {
    yield return
    [
      new ApplyAuraActionDefinition(
        "aura_001",
        PlayerSelector.Self,
        new AuraDefinition(
          "aura_attack_001",
          "BOARD_ATTACK_AURA",
          new EntityQuery("OWN", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
          new ModifierDefinition("aura_modifier_001", "BOARD_ATTACK_MODIFIER", new ValueExpression("FIXED", 1, null), "UNTIL_SOURCE_LEAVES_HAND", 0),
          "UNTIL_SOURCE_LEAVES_HAND",
          0))
    ];
    yield return
    [
      new ReplaceEventActionDefinition(
        "replace_001",
        "APPLY_DAMAGE",
        new TargetRef("targetRequirement"),
        "REPLACE_WITH_HEAL",
        "NEXT_EVENT",
        "replacement_event")
    ];
    yield return
    [
      new TakeExtraTurnActionDefinition(
        "extra_turn_unsupported_001",
        PlayerSelector.Self,
        OncePerGame: true,
        CurrentTurnEndsNormally: true,
        "AFTER_CURRENT_TURN",
        "QUEUE_AFTER_CURRENT_TURN",
        null,
        "EXPIRE_WITH_ORIGINAL_TURN")
    ];
  }

  [Fact]
  public void SemanticValidationRejectsPreventDamagePolicyThatRuntimeCannotCompile()
  {
    var action = new PreventDamageActionDefinition(
      "prevent-invalid-policy", new TargetRef("targetRequirement"),
      new ValueExpression("FIXED", 2, null), "NEXT_DAMAGE", "PREVENT_DAMAGE", "prevent-result");
    var candidate = TestFixtures.CreateBattlecryCandidate(extraAction: action);

    var result = new SemanticValidationStage().Validate(new GeneratedCardValidationRequest(
      "generation-invalid-prevention-policy", "pipeline-v1", "context-v1", candidate, CreateContext()));

    Assert.Equal(GeneratedCardValidationDecision.Rejected, result.Decision);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.RuntimeActionNotSupported &&
      diagnostic.Path.EndsWith(".preventionPolicy", StringComparison.Ordinal));
  }

  [Fact]
  public void SemanticValidationRejectsMissingActionTargetWithoutThrowing()
  {
    var action = new DealDamageActionDefinition(
      "damage-missing-target", new ValueExpression("FIXED", 1, null), null!, null, false, true);
    var candidate = TestFixtures.CreateBattlecryCandidate(extraAction: action);

    var result = new SemanticValidationStage().Validate(new GeneratedCardValidationRequest(
      "generation-missing-target", "pipeline-v1", "context-v1", candidate, CreateContext()));

    Assert.Equal(GeneratedCardValidationDecision.Rejected, result.Decision);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Path.EndsWith(".target", StringComparison.Ordinal));
  }

  private static GenerationContext CreateContext(
    IReadOnlyList<string>? allowedActions = null,
    IReadOnlyList<string>? allowedEffectKinds = null,
    int maxActionsPerEffect = 3,
    int maxEffectsPerCard = 2)
  {
    var profile = TestFixtures.CreateProfile(
      allowedActions: allowedActions ?? ["DEAL_DAMAGE", "DRAW_CARD"],
      allowedEffectKinds: allowedEffectKinds ?? ["SIMPLE_PLAY_EFFECT"],
      maxActionsPerEffect: maxActionsPerEffect,
      maxEffectsPerCard: maxEffectsPerCard);

    return new GenerationContextBuilder().Build(
      profile,
      [TestFixtures.Mechanic("BATTLECRY", MechanicStatus.ApprovedForRuleset)]);
  }
}

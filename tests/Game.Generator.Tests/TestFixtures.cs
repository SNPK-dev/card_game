using Game.Shared.Dsl;
using Game.Generator.Validation;
using Game.Shared.Mechanics;
using Game.Shared.Primitives;
using Game.Shared.Ruleset;

namespace Game.Generator.Tests;

internal static class TestFixtures
{
  public static MechanicStatusRecord Mechanic(
    string mechanicId,
    MechanicStatus status) =>
    new(
      mechanicId,
      status,
      MechanicTier.Tier0,
      "test",
      [],
      status is MechanicStatus.ApprovedForRuleset ? ["ruleset_mvp_core_v1"] : [],
      null,
      null,
      ["docs/generator/mechanic_status_spec.md"],
      [],
      [],
      []);

  public static ResolvedRulesetProfile CreateProfile(
    IReadOnlyList<string>? enabledMechanics = null,
    IReadOnlyList<string>? disabledMechanics = null,
    IReadOnlyList<FormatMechanicState>? formatMechanicStates = null,
    IReadOnlyList<string>? allowedActions = null,
    IReadOnlyList<string>? allowedEffectKinds = null,
    IReadOnlyList<string>? allowedCardTypes = null,
    IReadOnlyList<string>? allowedRarities = null,
    IReadOnlyList<string>? allowedClasses = null,
    IReadOnlyList<string>? allowedTribes = null,
    IReadOnlyList<string>? allowedKeywords = null,
    bool requireBalanceReport = false,
    IReadOnlyList<string>? autoRejectRiskFlags = null,
    IReadOnlyList<string>? requiredScenarioSets = null,
    IReadOnlyList<string>? requiredEdgeCases = null,
    int minRandomBotGames = 0,
    int minBaselineDeckGames = 0,
    bool requireReplayHashConsistency = false,
    bool requireSimulationPass = false,
    bool requireReplayPass = false,
    int maxActionsPerEffect = 3,
    int maxEffectsPerCard = 2) =>
    new(
      "ruleset_mvp_core_v1",
      "ruleset_mvp_core",
      "1.0.0",
      DateTimeOffset.Parse("2026-06-23T00:00:00Z"),
      "test",
      "resolved-hash",
      "mvp_internal",
      "core_generated_alpha",
      enabledMechanics ?? ["BATTLECRY"],
      disabledMechanics ?? [],
      formatMechanicStates ?? [],
      allowedEffectKinds ?? ["SIMPLE_PLAY_EFFECT"],
      allowedActions ?? ["DEAL_DAMAGE"],
      allowedKeywords ?? ["BATTLECRY"],
      ["HAND", "BOARD"],
      ["ON_PLAY"],
      ["FIXED_POOL"],
      allowedCardTypes ?? ["MINION_CARD"],
      ["MINION"],
      allowedRarities ?? ["RARE"],
      allowedClasses ?? ["mage"],
      allowedTribes ?? ["APPRENTICE"],
      new RulesetLimits(
        30,
        25,
        20,
        80,
        5,
        1,
        "LIMITED",
        maxActionsPerEffect,
        maxEffectsPerCard,
        3,
        3,
        3,
        2,
        2,
        3,
        1),
      new ValidationPolicyRef(
        "validation_mvp",
        SchemaValidation: true,
        DeserializationValidation: true,
        SemanticValidation: true,
        RuleValidation: true,
        StaticAnalysis: true,
        FailOnUnknownMechanic: true,
        FailOnDeprecatedMechanic: true,
        FailOnDisabledMechanic: true,
        AllowLegacyDeprecatedMechanics: false),
      new BalancePolicyRef(
        "balance_mvp",
        RequireBalanceReport: requireBalanceReport,
        RequireHumanReviewAboveRisk: "MEDIUM",
        autoRejectRiskFlags ?? [],
        0.05,
        30,
        25,
        0.1,
        0,
        0.0,
        "baseline",
        "seed",
        "none"),
      new SimulationPolicyRef(
        "simulation_mvp",
        requiredScenarioSets ?? [],
        requiredEdgeCases ?? [],
        minRandomBotGames,
        minBaselineDeckGames,
        RequireReplayHashConsistency: requireReplayHashConsistency,
        30,
        80),
      new PublicationPolicyRef(
        "publication_mvp",
        RequireValidationPass: true,
        RequireSimulationPass: requireSimulationPass,
        RequireReplayPass: requireReplayPass,
        RequireBalanceReview: false,
        RequireHumanApproval: true,
        AllowAutoPublish: false,
        AllowExperimentalPublication: false,
        ["INTERNAL"]),
      new LegacyPolicy(
        AllowDeprecatedMechanicsForReplay: true,
        AllowDeprecatedMechanicsForNewCards: false,
        AllowDisabledMechanicsForReplay: true,
        AllowDisabledMechanicsForNewCards: false,
        ReplayProfilePinningRequired: true));

  public static GeneratedCardBalanceReport CreateBalanceReport(
    IReadOnlyList<string>? riskFlags = null,
    double simulationWinrateDelta = 0.01,
    int complexityScore = 8,
    int randomnessSwingScore = 0,
    double eventCountDelta = 0.01) =>
    new(
      "balance-report-001",
      "balance_mvp",
      simulationWinrateDelta,
      complexityScore,
      randomnessSwingScore,
      eventCountDelta,
      riskFlags ?? []);

  public static GeneratedCardSimulationReport CreateSimulationReport(
    IReadOnlyList<string>? scenarioSets = null,
    IReadOnlyList<string>? edgeCases = null,
    int randomBotGames = 10,
    int baselineDeckGames = 10,
    bool replayHashConsistent = true,
    int maxObservedTurns = 12,
    int maxObservedEventCount = 30) =>
    new(
      "simulation-report-001",
      "simulation_mvp",
      scenarioSets ?? [],
      edgeCases ?? [],
      randomBotGames,
      baselineDeckGames,
      replayHashConsistent,
      maxObservedTurns,
      maxObservedEventCount);

  public static GeneratedCardReplayReport CreateReplayReport(
    bool replayPassed = true,
    bool finalStateHashMatched = true,
    int replayRuns = 2,
    string? finalStateHash = "sha256:final-state",
    IReadOnlyList<string>? checkedScenarioSets = null) =>
    new(
      "replay-report-001",
      "publication_mvp",
      replayPassed,
      finalStateHashMatched,
      replayRuns,
      finalStateHash,
      checkedScenarioSets ?? ["mvp-smoke"]);

  public static CardDefinition CreateBattlecryCandidate(
    IReadOnlyList<string>? effectTags = null,
    EffectKind effectKind = EffectKind.SimplePlayEffect,
    ActionDefinition? extraAction = null,
    string schemaVersion = "dsl-v1",
    string rulesetProfileId = "ruleset_mvp_core_v1",
    int complexityScore = 8,
    int randomnessScore = 0,
    IReadOnlyList<string>? riskFlags = null,
    IReadOnlyList<string>? classTags = null,
    IReadOnlyList<string>? tribes = null,
    IReadOnlyList<string>? keywords = null)
  {
    IReadOnlyList<ActionDefinition> actions = extraAction is null
      ?
      [
        new DealDamageActionDefinition(
          "damage_001",
          new ValueExpression("FIXED", 3, null),
          new TargetRef("targetRequirement"),
          "ARCANE",
          Lifesteal: false,
          Preventable: true)
      ]
      :
      [
        new DealDamageActionDefinition(
          "damage_001",
          new ValueExpression("FIXED", 3, null),
          new TargetRef("targetRequirement"),
          "ARCANE",
          Lifesteal: false,
          Preventable: true),
        extraAction
      ];

    return new CardDefinition(
      "card_arcane_apprentice",
      null,
      schemaVersion,
      "engine-v1",
      "primitive-v1",
      rulesetProfileId,
      null,
      new StaticCardData(
        "Arcane Apprentice",
        CardType.MinionCard,
        classTags ?? ["mage"],
        Rarity.Rare,
        2,
        tribes ?? ["APPRENTICE"],
        keywords ?? ["BATTLECRY"],
        Collectible: true,
        ["mvp"]),
      new EntityDefinition(
        new MinionDefinition(2, 2, ["APPRENTICE"], [], CanAttack: true),
        null,
        null,
        null,
        null,
        null),
      [
        new EffectDefinition(
          "battlecry_damage",
          effectKind,
          [SourceZone.Hand],
          null,
          new TimingPolicy(Phase.OnPlay, null, 0, "SOURCE_ORDER"),
          [],
          new TargetSpec(
            Required: true,
            TargetSelectionMode.PlayerChosen,
            new EntityQuery("ANY", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
            new CountSpec(1, 1),
            "FIZZLE_IF_INVALID"),
          actions,
          new ResolutionPolicy("SEQUENTIAL"),
          new FizzlePolicy("FIZZLE_WHOLE_EFFECT"),
          new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
          effectTags ?? ["BATTLECRY"])
      ],
      new CardTextGeneration("Battlecry: Deal 3 damage.", null),
      new BalanceMetadata(complexityScore, randomnessScore, riskFlags ?? []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata(
        "test",
        DateTimeOffset.Parse("2026-06-23T00:00:00Z"),
        "manual-fixture",
        ["docs/generator/dsl_reference.md"]));
  }
}

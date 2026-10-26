using Game.Generator.Generation;
using Game.Generator.Validation;
using Game.Shared.Dsl;
using Game.Shared.Mechanics;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Generator.Tests;

public sealed class GeneratedCardValidationPipelineTests
{
  [Fact]
  public void ValidateRunsDefaultStagesInDeterministicOrder()
  {
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate());

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.Equal(
      [
        GeneratedCardValidationStageKind.Schema,
        GeneratedCardValidationStageKind.Semantic,
        GeneratedCardValidationStageKind.Rule,
        GeneratedCardValidationStageKind.StaticAnalysis,
        GeneratedCardValidationStageKind.Balance,
        GeneratedCardValidationStageKind.Simulation,
        GeneratedCardValidationStageKind.Replay
      ],
      report.StageReports.Select(stage => stage.Stage));
    Assert.Equal(GeneratedCardValidationDecision.Accepted, report.FinalDecision);
    Assert.Equal("generation-001", report.GenerationId);
    Assert.Equal("pipeline-v1", report.PipelineVersion);
    Assert.Equal("ruleset_mvp_core_v1", report.RulesetProfileId);
    Assert.Equal("resolved-hash", report.RulesetProfileHash);
  }

  [Fact]
  public void ValidateReportsEveryStageWhenCandidateHasSchemaAndRuleErrors()
  {
    var candidate = TestFixtures.CreateBattlecryCandidate(
      schemaVersion: string.Empty,
      effectTags: ["SMART_OFFER"]);
    var request = CreateRequest(candidate);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.Equal(GeneratedCardValidationDecision.Rejected, report.FinalDecision);
    Assert.Equal(7, report.StageReports.Count);
    Assert.Contains(report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Schema).Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.RequiredFieldMissing
      && diagnostic.Path == "card.schemaVersion");
    Assert.Contains(report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Rule).Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.MechanicNotAvailable
      && diagnostic.ActualValue == "SMART_OFFER");
  }

  [Fact]
  public void SemanticStageRejectsDuplicateActionIdentifiers()
  {
    var duplicateAction = new DrawCardActionDefinition(
      "damage_001",
      PlayerSelector.Self,
      new ValueExpression("FIXED", 1, null),
      "SKIP_ACTION",
      null);
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate(extraAction: duplicateAction));

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.Equal(GeneratedCardValidationDecision.Rejected, report.FinalDecision);
    Assert.Contains(report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Semantic).Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.DuplicateIdentifier
      && diagnostic.ActualValue == "damage_001");
  }

  [Fact]
  public void SemanticStageRejectsUnsupportedDamageDistributionPolicy()
  {
    var action = new DamageDistributionActionDefinition(
      "damage_distribution_001",
      new ValueExpression("FIXED", 5, null),
      new EntityQuery("ANY", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
      "RANDOM_SPLIT",
      "SEPARATE_DAMAGE_INSTANCES",
      "HEAL_PER_INSTANCE",
      "ALLOW_OVERKILL",
      FizzleIfNoTargets: true,
      "RANDOM_TARGET");
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate(extraAction: action));

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.Equal(GeneratedCardValidationDecision.Rejected, report.FinalDecision);
    Assert.Contains(report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Semantic).Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.DamageDistributionNotSupported
      && diagnostic.Path.EndsWith(".lifestealAggregationPolicy", StringComparison.Ordinal));
  }

  [Fact]
  public void SemanticStageAcceptsSupportedDamageDistributionLifecyclePolicies()
  {
    var action = new DamageDistributionActionDefinition(
      "damage_distribution_001",
      new ValueExpression("FIXED", 5, null),
      new EntityQuery("ANY", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
      "SOURCE_ORDER",
      "SEPARATE_DAMAGE_INSTANCES",
      "AGGREGATE_LIFESTEAL",
      "CAP_AT_REMAINING_HEALTH",
      FizzleIfNoTargets: true,
      "NONE");
    var profile = TestFixtures.CreateProfile(allowedActions: ["DEAL_DAMAGE", "DAMAGE_DISTRIBUTION"]);
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate(extraAction: action), profile);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.DoesNotContain(
      report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Semantic).Diagnostics,
      diagnostic => diagnostic.Code == GeneratedCardValidationCode.DamageDistributionNotSupported);
    Assert.Equal(GeneratedCardValidationDecision.Accepted, report.FinalDecision);
  }

  [Fact]
  public void SemanticStageAcceptsSupportedApplyAuraPolicy()
  {
    var action = new ApplyAuraActionDefinition(
      "aura_001",
      PlayerSelector.Self,
      new AuraDefinition(
        "aura-board-attack",
        "BOARD_STAT_AURA",
        new EntityQuery("SELF", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
        new ModifierDefinition(
          "modifier-aura-attack",
          "BOARD_ATTACK_MODIFIER",
          new ValueExpression("FIXED", 1, null),
          "WHILE_SOURCE_ACTIVE",
          Priority: 10),
        "WHILE_SOURCE_ACTIVE",
        Priority: 10));
    var profile = TestFixtures.CreateProfile(allowedActions: ["DEAL_DAMAGE", "APPLY_AURA"]);
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate(extraAction: action), profile);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.DoesNotContain(
      report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Semantic).Diagnostics,
      diagnostic => diagnostic.Code == GeneratedCardValidationCode.RuntimeActionNotSupported);
    Assert.Equal(GeneratedCardValidationDecision.Accepted, report.FinalDecision);
  }

  [Fact]
  public void SemanticStageAcceptsSupportedReplaceEventPolicy()
  {
    var action = new ReplaceEventActionDefinition(
      "replace_001",
      "DAMAGE_WOULD_BE_DEALT",
      new TargetRef("TARGET"),
      "PREVENT_DAMAGE",
      "NEXT_DAMAGE",
      ResultRef: null);
    var profile = TestFixtures.CreateProfile(allowedActions: ["DEAL_DAMAGE", "REPLACE_EVENT"]);
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate(extraAction: action), profile);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.DoesNotContain(
      report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Semantic).Diagnostics,
      diagnostic => diagnostic.Code == GeneratedCardValidationCode.RuntimeActionNotSupported);
    Assert.Equal(GeneratedCardValidationDecision.Accepted, report.FinalDecision);
  }

  [Fact]
  public void SemanticStageRejectsUnsupportedResurrectHistoryQuery()
  {
    var action = new ResurrectActionDefinition(
      "resurrect_001",
      new TargetRef("HISTORY_QUERY"),
      PlayerSelector.Self,
      new ValueExpression("FIXED", 2, null),
      new ValueExpression("FIXED", 3, null),
      "APPEND",
      "SKIP_ACTION",
      ResultRef: null,
      new HistoryQuerySpec(
        "ALL_DEAD_MINIONS",
        PlayerSelector.Self,
        "RANDOM",
        MaxCandidates: 0));
    var profile = TestFixtures.CreateProfile(allowedActions: ["DEAL_DAMAGE", "RESURRECT"]);
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate(extraAction: action), profile);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    var diagnostics = report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Semantic).Diagnostics;
    Assert.Equal(GeneratedCardValidationDecision.Rejected, report.FinalDecision);
    Assert.Contains(diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.HistoryQueryNotSupported
      && diagnostic.Path == "card.effects[0].actions[1].historyQuery.kind");
    Assert.Contains(diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.HistoryQueryNotSupported
      && diagnostic.Path == "card.effects[0].actions[1].historyQuery.maxCandidates");
  }

  [Fact]
  public void SemanticStageAcceptsAnyDeadMinionResurrectHistoryQuery()
  {
    var action = new ResurrectActionDefinition(
      "resurrect_001",
      new TargetRef("HISTORY_QUERY"),
      PlayerSelector.Self,
      new ValueExpression("FIXED", 2, null),
      new ValueExpression("FIXED", 3, null),
      "APPEND",
      "SKIP_ACTION",
      ResultRef: null,
      new HistoryQuerySpec(
        "ANY_DEAD_MINION",
        PlayerSelector.Self,
        "LAST",
        MaxCandidates: 10));
    var profile = TestFixtures.CreateProfile(allowedActions: ["DEAL_DAMAGE", "RESURRECT"]);
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate(extraAction: action), profile);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.DoesNotContain(
      report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Semantic).Diagnostics,
      diagnostic => diagnostic.Code == GeneratedCardValidationCode.HistoryQueryNotSupported);
    Assert.Equal(GeneratedCardValidationDecision.Accepted, report.FinalDecision);
  }

  [Fact]
  public void SemanticStageAcceptsRandomResurrectHistoryQueryOrdering()
  {
    var action = new ResurrectActionDefinition(
      "resurrect_001",
      new TargetRef("HISTORY_QUERY"),
      PlayerSelector.Self,
      new ValueExpression("FIXED", 2, null),
      new ValueExpression("FIXED", 3, null),
      "APPEND",
      "SKIP_ACTION",
      ResultRef: null,
      new HistoryQuerySpec(
        "OWN_GRAVEYARD_MINION",
        PlayerSelector.Self,
        "RANDOM",
        MaxCandidates: 10));
    var profile = TestFixtures.CreateProfile(allowedActions: ["DEAL_DAMAGE", "RESURRECT"]);
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate(extraAction: action), profile);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.DoesNotContain(
      report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Semantic).Diagnostics,
      diagnostic => diagnostic.Code == GeneratedCardValidationCode.HistoryQueryNotSupported);
  }

  [Fact]
  public void SemanticStageAcceptsOpponentResurrectHistoryQuery()
  {
    var action = new ResurrectActionDefinition(
      "resurrect_001",
      new TargetRef("HISTORY_QUERY"),
      PlayerSelector.Opponent,
      new ValueExpression("FIXED", 2, null),
      new ValueExpression("FIXED", 3, null),
      "APPEND",
      "SKIP_ACTION",
      ResultRef: null,
      new HistoryQuerySpec(
        "OPPONENT_GRAVEYARD_MINION",
        PlayerSelector.Opponent,
        "LAST",
        MaxCandidates: 10));
    var profile = TestFixtures.CreateProfile(allowedActions: ["DEAL_DAMAGE", "RESURRECT"]);
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate(extraAction: action), profile);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.DoesNotContain(
      report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Semantic).Diagnostics,
      diagnostic => diagnostic.Code == GeneratedCardValidationCode.HistoryQueryNotSupported);
  }

  [Fact]
  public void SemanticStageAcceptsAnyGraveyardResurrectHistoryQuery()
  {
    var action = new ResurrectActionDefinition(
      "resurrect_001",
      new TargetRef("HISTORY_QUERY"),
      PlayerSelector.Self,
      new ValueExpression("FIXED", 2, null),
      new ValueExpression("FIXED", 3, null),
      "APPEND",
      "SKIP_ACTION",
      ResultRef: null,
      new HistoryQuerySpec(
        "ANY_GRAVEYARD_MINION",
        PlayerSelector.Self,
        "LOWEST_CARD_INSTANCE_ID",
        MaxCandidates: 10));
    var profile = TestFixtures.CreateProfile(allowedActions: ["DEAL_DAMAGE", "RESURRECT"]);
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate(extraAction: action), profile);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.DoesNotContain(
      report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Semantic).Diagnostics,
      diagnostic => diagnostic.Code == GeneratedCardValidationCode.HistoryQueryNotSupported);
  }

  [Fact]
  public void SemanticStageAcceptsHandZoneModifierValue()
  {
    var action = new ApplyModifierActionDefinition(
      "modifier_001",
      PlayerSelector.Self,
      new ModifierDefinition(
        "modifier_hand_zone",
        "HAND_ZONE_MODIFIER",
        new ValueExpression("ZONE", FixedValue: null, Ref: "GRAVEYARD"),
        "PERMANENT",
        Priority: 10));
    var profile = TestFixtures.CreateProfile(allowedActions: ["DEAL_DAMAGE", "APPLY_MODIFIER"]);
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate(extraAction: action), profile);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.DoesNotContain(
      report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Semantic).Diagnostics,
      diagnostic => diagnostic.Code == GeneratedCardValidationCode.IncompleteValueExpression);
    Assert.Equal(GeneratedCardValidationDecision.Accepted, report.FinalDecision);
  }

  [Fact]
  public void SemanticStageRejectsMinionWithoutStats()
  {
    var candidate = TestFixtures.CreateBattlecryCandidate() with
    {
      Entity = new EntityDefinition(
        new MinionDefinition(0, 0, [], [], CanAttack: true),
        null,
        null,
        null,
        null,
        null)
    };

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(CreateRequest(candidate));

    Assert.Equal(GeneratedCardValidationDecision.Rejected, report.FinalDecision);
    Assert.Contains(report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Semantic).Diagnostics,
      diagnostic => diagnostic.Code == GeneratedCardValidationCode.SemanticContractMismatch
                    && diagnostic.Path == "card.entity.minion");
  }

  [Fact]
  public void SemanticStageRejectsSpellTextActionWithoutDslAction()
  {
    var candidate = TestFixtures.CreateBattlecryCandidate() with
    {
      StaticData = TestFixtures.CreateBattlecryCandidate().StaticData with { CardType = CardType.SpellCard },
      Entity = new EntityDefinition(null, new SpellDefinition(null, RequiresTarget: false), null, null, null, null),
      Effects = [],
      CardText = new CardTextGeneration("Deal 3 damage to a minion.", FlavorText: null)
    };

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(CreateRequest(candidate));

    Assert.Equal(GeneratedCardValidationDecision.Rejected, report.FinalDecision);
    Assert.Contains(report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Semantic).Diagnostics,
      diagnostic => diagnostic.Code == GeneratedCardValidationCode.SemanticContractMismatch
                    && diagnostic.Path == "card.effects");
  }

  [Fact]
  public void SemanticStageRejectsTargetRequirementRefWithoutTargetRequirement()
  {
    var candidate = TestFixtures.CreateBattlecryCandidate() with
    {
      Effects =
      [
        TestFixtures.CreateBattlecryCandidate().Effects[0] with
        {
          TargetRequirement = null,
          Actions =
          [
            new DealDamageActionDefinition(
              "damage_without_target_requirement",
              new ValueExpression("FIXED", 1, null),
              new TargetRef("targetRequirement"),
              DamageSchool: null,
              Lifesteal: false,
              Preventable: true)
          ]
        }
      ]
    };

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(CreateRequest(candidate));

    Assert.Equal(GeneratedCardValidationDecision.Rejected, report.FinalDecision);
    Assert.Contains(report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Semantic).Diagnostics,
      diagnostic => diagnostic.Code == GeneratedCardValidationCode.SemanticContractMismatch
                    && diagnostic.Path.EndsWith(".target.ref", StringComparison.Ordinal));
  }

  [Fact]
  public void SemanticStageRejectsLocationCardWithoutLocationAbilityEffect()
  {
    var candidate = TestFixtures.CreateBattlecryCandidate() with
    {
      StaticData = TestFixtures.CreateBattlecryCandidate().StaticData with { CardType = CardType.LocationCard },
      Entity = new EntityDefinition(null, null, null, new LocationDefinition(2, "ONCE_PER_TURN", "DURABILITY"), null, null),
      Effects = [TestFixtures.CreateBattlecryCandidate().Effects[0] with { EffectKind = EffectKind.SimplePlayEffect }],
      CardText = new CardTextGeneration("Location ability: Draw a card.", FlavorText: null)
    };

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(CreateRequest(candidate));

    Assert.Equal(GeneratedCardValidationDecision.Rejected, report.FinalDecision);
    Assert.Contains(report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Semantic).Diagnostics,
      diagnostic => diagnostic.Code == GeneratedCardValidationCode.SemanticContractMismatch
                    && diagnostic.Path == "card.effects");
  }

  [Fact]
  public void SemanticStageRejectsDeathrattleEffectWithoutDeathTrigger()
  {
    var candidate = TestFixtures.CreateBattlecryCandidate() with
    {
      Effects =
      [
        TestFixtures.CreateBattlecryCandidate().Effects[0] with
        {
          EffectKind = EffectKind.DeathrattleEffect,
          Trigger = new TriggerDefinition(EventType.CardPlayed, null, "SOURCE_CONTROLLER", OncePerEvent: false),
          TimingPolicy = new TimingPolicy(Phase.OnPlay, null, 0, "CARD_TEXT_ORDER"),
          SourceZones = [SourceZone.Hand]
        }
      ],
      CardText = new CardTextGeneration("Deathrattle: Draw a card.", FlavorText: null)
    };

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(CreateRequest(candidate));

    Assert.Equal(GeneratedCardValidationDecision.Rejected, report.FinalDecision);
    Assert.Contains(report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Semantic).Diagnostics,
      diagnostic => diagnostic.Code == GeneratedCardValidationCode.SemanticContractMismatch
                    && diagnostic.Path == "card.effects.deathrattle");
  }

  [Fact]
  public void StaticAnalysisWarningDoesNotRejectOtherwiseValidCandidate()
  {
    var candidate = TestFixtures.CreateBattlecryCandidate(complexityScore: 24);
    var request = CreateRequest(candidate);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.Equal(GeneratedCardValidationDecision.Accepted, report.FinalDecision);
    Assert.Contains(report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.StaticAnalysis).Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.StaticAnalysisReviewRecommended
      && diagnostic.Severity == GeneratedCardValidationSeverity.Warning);
  }

  [Fact]
  public void BalanceStageRejectsMissingRequiredBalanceReport()
  {
    var profile = TestFixtures.CreateProfile(
      allowedActions: ["DEAL_DAMAGE", "DRAW_CARD"],
      requireBalanceReport: true);
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate(), profile, includeBalanceReport: false);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.Equal(GeneratedCardValidationDecision.Rejected, report.FinalDecision);
    Assert.Contains(report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Balance).Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.BalanceReportRequired);
  }

  [Fact]
  public void BalanceStageRejectsAutoRejectedRiskFlags()
  {
    var profile = TestFixtures.CreateProfile(
      allowedActions: ["DEAL_DAMAGE", "DRAW_CARD"],
      autoRejectRiskFlags: ["UNBOUNDED_LOOP"]);
    var candidate = TestFixtures.CreateBattlecryCandidate(riskFlags: ["UNBOUNDED_LOOP"]);
    var request = CreateRequest(candidate, profile);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.Equal(GeneratedCardValidationDecision.Rejected, report.FinalDecision);
    Assert.Contains(report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Balance).Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.RiskFlagAutoRejected
      && diagnostic.ActualValue == "UNBOUNDED_LOOP");
  }

  [Fact]
  public void BalanceStageRejectsReportThresholdViolations()
  {
    var request = CreateRequest(
      TestFixtures.CreateBattlecryCandidate(),
      balanceReport: TestFixtures.CreateBalanceReport(
        simulationWinrateDelta: 0.2,
        complexityScore: 31,
        randomnessSwingScore: 26,
        eventCountDelta: 0.2));

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    var diagnostics = report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Balance).Diagnostics;
    Assert.Equal(GeneratedCardValidationDecision.Rejected, report.FinalDecision);
    Assert.Contains(diagnostics, diagnostic => diagnostic.Code == GeneratedCardValidationCode.WinrateDeltaLimitExceeded);
    Assert.Contains(diagnostics, diagnostic => diagnostic.Code == GeneratedCardValidationCode.ComplexityLimitExceeded);
    Assert.Contains(diagnostics, diagnostic => diagnostic.Code == GeneratedCardValidationCode.RandomnessLimitExceeded);
    Assert.Contains(diagnostics, diagnostic => diagnostic.Code == GeneratedCardValidationCode.EventCountDeltaLimitExceeded);
  }

  [Fact]
  public void SimulationStageRejectsMissingRequiredSimulationReport()
  {
    var profile = TestFixtures.CreateProfile(
      allowedActions: ["DEAL_DAMAGE", "DRAW_CARD"],
      requiredScenarioSets: ["mvp-smoke"],
      requiredEdgeCases: ["full-board"],
      minRandomBotGames: 5,
      minBaselineDeckGames: 5,
      requireReplayHashConsistency: true,
      requireSimulationPass: true);
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate(), profile, includeSimulationReport: false);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.Equal(GeneratedCardValidationDecision.Rejected, report.FinalDecision);
    Assert.Contains(report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Simulation).Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.SimulationReportRequired);
  }

  [Fact]
  public void SimulationStageRejectsInsufficientSimulationEvidence()
  {
    var profile = TestFixtures.CreateProfile(
      allowedActions: ["DEAL_DAMAGE", "DRAW_CARD"],
      requiredScenarioSets: ["mvp-smoke"],
      requiredEdgeCases: ["full-board"],
      minRandomBotGames: 5,
      minBaselineDeckGames: 5,
      requireReplayHashConsistency: true);
    var simulationReport = TestFixtures.CreateSimulationReport(
      scenarioSets: [],
      edgeCases: [],
      randomBotGames: 2,
      baselineDeckGames: 3,
      replayHashConsistent: false,
      maxObservedTurns: 31,
      maxObservedEventCount: 81);
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate(), profile, simulationReport: simulationReport);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    var diagnostics = report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Simulation).Diagnostics;
    Assert.Equal(GeneratedCardValidationDecision.Rejected, report.FinalDecision);
    Assert.Contains(diagnostics, diagnostic => diagnostic.Code == GeneratedCardValidationCode.SimulationScenarioSetMissing);
    Assert.Contains(diagnostics, diagnostic => diagnostic.Code == GeneratedCardValidationCode.SimulationEdgeCaseMissing);
    Assert.Contains(diagnostics, diagnostic => diagnostic.Code == GeneratedCardValidationCode.SimulationGameCountInsufficient);
    Assert.Contains(diagnostics, diagnostic => diagnostic.Code == GeneratedCardValidationCode.ReplayHashConsistencyRequired);
    Assert.Contains(diagnostics, diagnostic => diagnostic.Code == GeneratedCardValidationCode.SimulationTurnLimitExceeded);
    Assert.Contains(diagnostics, diagnostic => diagnostic.Code == GeneratedCardValidationCode.SimulationEventLimitExceeded);
  }

  [Fact]
  public void ReplayStageRejectsMissingRequiredReplayReport()
  {
    var profile = TestFixtures.CreateProfile(
      allowedActions: ["DEAL_DAMAGE", "DRAW_CARD"],
      requireReplayPass: true);
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate(), profile, includeReplayReport: false);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.Equal(GeneratedCardValidationDecision.Rejected, report.FinalDecision);
    Assert.Contains(report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Replay).Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.ReplayReportRequired);
  }

  [Fact]
  public void ReplayStageRejectsInsufficientReplayEvidence()
  {
    var replayReport = TestFixtures.CreateReplayReport(
      replayPassed: false,
      finalStateHashMatched: false,
      replayRuns: 1,
      finalStateHash: null);
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate(), replayReport: replayReport);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    var diagnostics = report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Replay).Diagnostics;
    Assert.Equal(GeneratedCardValidationDecision.Rejected, report.FinalDecision);
    Assert.Contains(diagnostics, diagnostic => diagnostic.Code == GeneratedCardValidationCode.ReplayPassRequired);
    Assert.Contains(diagnostics, diagnostic => diagnostic.Code == GeneratedCardValidationCode.ReplayFinalStateHashMismatch);
    Assert.Contains(diagnostics, diagnostic => diagnostic.Code == GeneratedCardValidationCode.ReplayRunCountInsufficient);
  }

  [Fact]
  public void ReplayStageRejectsPassedReportWithoutFinalStateHash()
  {
    var replayReport = TestFixtures.CreateReplayReport(finalStateHash: " ");
    var request = CreateRequest(TestFixtures.CreateBattlecryCandidate(), replayReport: replayReport);

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    var diagnostics = report.StageReports.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Replay).Diagnostics;
    Assert.Equal(GeneratedCardValidationDecision.Rejected, report.FinalDecision);
    Assert.Contains(diagnostics, diagnostic => diagnostic.Code == GeneratedCardValidationCode.ReplayFinalStateHashRequired);
  }

  private static GeneratedCardValidationRequest CreateRequest(
    CardDefinition candidate,
    Game.Shared.Ruleset.ResolvedRulesetProfile? profile = null,
    GeneratedCardBalanceReport? balanceReport = null,
    GeneratedCardSimulationReport? simulationReport = null,
    GeneratedCardReplayReport? replayReport = null,
    bool includeBalanceReport = true,
    bool includeSimulationReport = true,
    bool includeReplayReport = true)
  {
    profile ??= TestFixtures.CreateProfile(allowedActions: ["DEAL_DAMAGE", "DRAW_CARD"]);
    var context = new GenerationContextBuilder().Build(
      profile,
      [TestFixtures.Mechanic("BATTLECRY", MechanicStatus.ApprovedForRuleset)]);

    return new GeneratedCardValidationRequest(
      "generation-001",
      "pipeline-v1",
      "context-v1",
      candidate,
      context,
      includeBalanceReport ? balanceReport ?? TestFixtures.CreateBalanceReport() : null,
      includeSimulationReport ? simulationReport ?? TestFixtures.CreateSimulationReport() : null,
      includeReplayReport ? replayReport ?? TestFixtures.CreateReplayReport() : null);
  }
}

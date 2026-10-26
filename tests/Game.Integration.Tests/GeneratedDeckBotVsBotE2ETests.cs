using Game.Application.Capabilities;
using Game.Application.Catalog;
using Game.Application.Decks;
using Game.Application.Matchmaking;
using Game.Application.Reconstruction;
using Game.Application.Rulesets;
using Game.Application.Simulation;
using Game.Domain.Commands;
using Game.Domain.State;
using Game.Generator.Acceptance;
using Game.Generator.Effects;
using Game.Generator.Generation;
using Game.Generator.Publication;
using Game.Generator.Providers;
using Game.Generator.Skeletons;
using Game.Generator.Validation;
using Game.Shared.Assets;
using Game.Shared.Catalog;
using Game.Shared.Decks;
using Game.Shared.Dsl;
using Game.Shared.Events;
using Game.Shared.Mechanics;
using Game.Shared.Primitives;
using Game.Shared.Ruleset;
using Xunit;

namespace Game.Integration.Tests;

public sealed class GeneratedDeckBotVsBotE2ETests
{
  private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-06-25T00:00:00Z");

  [Fact]
  public void GeneratedMinimumDecksCanRunDeterministicBotVsBotMatch()
  {
    var profile = CreateProfile();
    var generatedCards = new[]
    {
      GenerateAndPublish(
        profile,
        "generation-bot-e2e-001",
        "card_arcane_apprentice",
        "Arcane Apprentice",
        "card_arcane_apprentice_v1",
        attack: 2,
        health: 2),
      GenerateAndPublish(
        profile,
        "generation-bot-e2e-002",
        "card_river_apprentice",
        "River Apprentice",
        "card_river_apprentice_v1",
        attack: 1,
        health: 3)
    };
    var assetManifests = generatedCards.SelectMany(CreateAcceptanceAssetManifests).ToArray();
    var catalog = new CatalogSnapshot(
      "catalog-bot-e2e",
      profile.SourceProfileId,
      profile.ResolvedHash,
      FixedNow,
      generatedCards);
    var services = CreateApplicationServices(profile, catalog);
    services.DeckStore.Store(CreateDeck("deck-bot-alpha", "player-1", catalog));
    services.DeckStore.Store(CreateDeck("deck-bot-beta", "player-2", catalog));

    Assert.True(services.Readiness.Check(new MatchReadinessRequest("deck-bot-alpha")).IsReady);
    Assert.True(services.Readiness.Check(new MatchReadinessRequest("deck-bot-beta")).IsReady);

    var match = services.MatchStart.Start(new MatchStartRequest(
      "game-bot-vs-bot-e2e",
      ["deck-bot-alpha", "deck-bot-beta"],
      "player-1"));
    Assert.True(match.Succeeded);

    var simulation = new BotSimulationRunner().Run(new BotSimulationRequest(
      "simulation-bot-vs-bot-e2e",
      Assert.IsType<InMemoryGameState>(match.State),
      catalog.Cards,
      new Dictionary<string, IBotAgent>(StringComparer.Ordinal)
      {
        ["player-1"] = new OraclePriorityBot(),
        ["player-2"] = new OraclePriorityBot()
      },
      new BotSimulationOptions(OpeningHandSize: 2, MaxCommands: 100, BotSeed: 1201),
      profile,
      new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry()));

    Assert.True(simulation.ReplayVerification.IsVerified);
    Assert.Equal(simulation.FinalStateHash, simulation.ReplayVerification.FinalStateHash);
    Assert.True(simulation.TerminalState.IsTerminal);
    Assert.Equal("HERO_DEFEATED", simulation.TerminalState.Reason);
    Assert.NotNull(simulation.TerminalState.WinningPlayerId);
    Assert.Contains(simulation.DerivedEvents, committedEvent =>
      committedEvent.EventType == CommittedEventType.GameEnded);
    Assert.Contains(simulation.CommandLog.Payloads, payload =>
      payload.CommandType == AttackCommand.CommandKind);
    Assert.Equal(
      Enumerable.Range(1, simulation.CommandLog.Payloads.Count),
      simulation.CommandLog.Payloads.Select(payload => payload.CommandSeq));
    Assert.Equal(2, simulation.ReplayVerificationRuns);
    Assert.NotEmpty(simulation.BotDecisionRolls ?? []);
    Assert.All(simulation.BotDecisionRolls ?? [], roll =>
      Assert.Equal(Game.Domain.Randomness.RngStreamKind.BotDecision, roll.Stream));

    var evidenceReport = BuildEvidenceReport(
      simulation,
      scenarioSets: ["smoke"],
      edgeCases: ["terminal_win_loss"],
      randomBotGames: 1,
      baselineDeckGames: 1);
    Assert.True(evidenceReport.SimulationReport.ReplayHashConsistent);
    Assert.NotNull(evidenceReport.SimulationReport.MatchEvidence);
    var matchEvidence = Assert.Single(evidenceReport.SimulationReport.MatchEvidence);
    Assert.Equal(simulation.FinalStateHash, matchEvidence.FinalStateHash);
    Assert.Equal(simulation.CommandLog.Payloads.Count, matchEvidence.CommandCount);
    Assert.Equal(simulation.DerivedEvents.Count, matchEvidence.EventCount);
    Assert.Equal("HERO_DEFEATED", matchEvidence.TerminalReason);
    Assert.Equal(2, matchEvidence.ReplayRuns);
    Assert.True(evidenceReport.ReplayReport.ReplayPassed);
    Assert.True(evidenceReport.ReplayReport.FinalStateHashMatched);
    Assert.Equal(simulation.FinalStateHash, evidenceReport.ReplayReport.FinalStateHash);
    Assert.Equal(2, evidenceReport.ReplayReport.ReplayRuns);

    var strictProfile = profile with
    {
      SimulationPolicy = new SimulationPolicyRef(
        "simulation_production_evidence",
        ["smoke"],
        ["terminal_win_loss"],
        MinRandomBotGames: 1,
        MinBaselineDeckGames: 1,
        RequireReplayHashConsistency: true,
        MaxTurns: 50,
        MaxEventCount: 500),
      PublicationPolicy = profile.PublicationPolicy with
      {
        RequireSimulationPass = true,
        RequireReplayPass = true
      }
    };
    var strictContext = new GenerationContextBuilder().Build(
      strictProfile,
      [CreateMechanic("BATTLECRY", MechanicStatus.ApprovedForRuleset)]);
    var strictValidation = GeneratedCardValidationPipeline.CreateDefault().Validate(
      new GeneratedCardValidationRequest(
        "generation-bot-e2e-001-production-evidence",
        "pipeline-v1",
        "context-v1",
        generatedCards[0].Definition,
        strictContext,
        BalanceReport: null,
        SimulationReport: evidenceReport.SimulationReport,
        ReplayReport: evidenceReport.ReplayReport));
    Assert.True(
      strictValidation.FinalDecision is GeneratedCardValidationDecision.Accepted,
      string.Join(
        Environment.NewLine,
        strictValidation.StageReports.SelectMany(stage =>
          stage.Diagnostics.Select(diagnostic =>
            $"{stage.Stage}:{diagnostic.Code}:{diagnostic.Path}:{diagnostic.ActualValue}"))));

    var finalState = Assert.IsType<InMemoryGameState>(simulation.FinalState);
    Assert.Empty(finalState.Players["player-1"].Zones.Deck);
    Assert.True(finalState.Players["player-1"].Zones.Hand.Count<=1);
    Assert.Empty(finalState.Players["player-2"].Zones.Deck);
    Assert.True(finalState.Players["player-2"].Zones.Hand.Count<=1);
    Assert.True(finalState.TerminalState.IsTerminal);
    Assert.Equal("HERO_DEFEATED", finalState.TerminalState.Reason);
    Assert.Contains(simulation.CommandLog.Payloads, payload =>
      payload.CommandType == PlayCardCommand.CommandKind);
    Assert.Contains(simulation.DerivedEvents, committedEvent =>
      committedEvent.EventType == CommittedEventType.MinionSummoned);

    var reconstruction = new MatchReconstructionService().Reconstruct(new MatchReconstructionRequest(
      "game-bot-vs-bot-e2e",
      simulation.CommandLog,
      simulation.DerivedEvents,
      simulation.FinalState,
      simulation.FinalStateHash,
      assetManifests));
    Assert.Equal(simulation.CommandLog.Payloads.Count, reconstruction.CommandCount);
    Assert.Equal(simulation.DerivedEvents.Count, reconstruction.EventCount);
    Assert.Equal(simulation.FinalStateHash, reconstruction.FinalStateHash);
    Assert.Equal("HERO_DEFEATED", reconstruction.TerminalReason);
    Assert.Contains(reconstruction.Turns.SelectMany(turn => turn.Commands), command =>
      command.CommandType == AttackCommand.CommandKind);
    Assert.Contains(
      reconstruction.Turns.SelectMany(turn => turn.Commands).SelectMany(command => command.Deltas),
      delta => delta.DeltaType == "DAMAGE_DELTA");
    Assert.Equal(assetManifests.Length, reconstruction.AssetRefs.Count);

    var acceptanceReport = new ProductionAcceptanceReportBuilder().Build(new ProductionAcceptanceReportRequest(
      "acceptance-report-generated-deck-bot-vs-bot",
      FixedNow,
      generatedCards,
      [
        CreateDeck("deck-bot-alpha", "player-1", catalog),
        CreateDeck("deck-bot-beta", "player-2", catalog)
      ],
      assetManifests,
      new AcceptedMatchEvidence(
        reconstruction.MatchId,
        simulation.SimulationId,
        simulation.FinalStateHash,
        simulation.TerminalState.Reason,
        simulation.TerminalState.WinningPlayerId,
        simulation.CommandLog.Payloads.Count,
        simulation.DerivedEvents.Count,
        simulation.ReplayVerificationRuns,
        simulation.ReplayVerification.IsVerified,
        reconstruction.Turns.Count,
        reconstruction.AssetRefs.Select(asset => asset.AssetManifestId).ToArray()),
      generatedCards.Select(CreateAcceptanceAuditEntry).ToArray(),
      CreateBlockedProviderHarnessResult(),
      ["real provider configuration is absent in local test environment"]));
    Assert.Equal(ProductionAcceptanceStatus.Blocked, acceptanceReport.Status);
    Assert.Equal(generatedCards.Length, acceptanceReport.GeneratedCards.Count);
    Assert.All(acceptanceReport.GeneratedCards, card =>
      Assert.NotEmpty(card.AssetManifestRefs));
    Assert.Equal(simulation.FinalStateHash, acceptanceReport.Match.FinalStateHash);
    Assert.True(acceptanceReport.Match.ReplayVerified);
    Assert.Equal(ProductionCardGenerationStatus.Blocked, acceptanceReport.ProviderEvidence.Status);
    Assert.NotEmpty(acceptanceReport.ProviderEvidence.Diagnostics);
  }

  [Fact]
  public void GeneratedComplexOnPlayEffectsExecuteInDeterministicBotVsBotReplay()
  {
    var profile = CreateProfile();
    var generatedCards = new[]
    {
      GenerateAndPublish(
        profile,
        "generation-complex-e2e-001",
        "card_01_anchor_apprentice",
        "Anchor Apprentice",
        "card_01_anchor_apprentice_v1",
        attack: 2,
        health: 5,
        catalogSnapshotId: "catalog-complex-bot-e2e"),
      GenerateAndPublish(
        profile,
        "generation-complex-e2e-002",
        "card_02_channel_sage",
        "Channel Sage",
        "card_02_channel_sage_v1",
        attack: 1,
        health: 4,
        catalogSnapshotId: "catalog-complex-bot-e2e",
        includeComplexOnPlayEffect: true),
      GenerateAndPublish(
        profile,
        "generation-complex-e2e-003",
        "card_03_reserve_apprentice",
        "Reserve Apprentice",
        "card_03_reserve_apprentice_v1",
        attack: 1,
        health: 2,
        catalogSnapshotId: "catalog-complex-bot-e2e")
    };
    var catalog = new CatalogSnapshot(
      "catalog-complex-bot-e2e",
      profile.SourceProfileId,
      profile.ResolvedHash,
      FixedNow,
      generatedCards);
    var services = CreateApplicationServices(profile, catalog);
    services.DeckStore.Store(CreateDeck("deck-complex-alpha", "player-1", catalog));
    services.DeckStore.Store(CreateDeck("deck-complex-beta", "player-2", catalog));

    var match = services.MatchStart.Start(new MatchStartRequest(
      "game-complex-bot-vs-bot-e2e",
      ["deck-complex-alpha", "deck-complex-beta"],
      "player-1"));
    Assert.True(
      match.Succeeded,
      string.Join(
        Environment.NewLine,
        match.Diagnostics.Select(diagnostic =>
          $"{diagnostic.Code}:{diagnostic.Path}:{diagnostic.Identifier}:{diagnostic.Message}")));

    var simulation = new BotSimulationRunner().Run(new BotSimulationRequest(
      "simulation-complex-bot-vs-bot-e2e",
      Assert.IsType<InMemoryGameState>(match.State),
      catalog.Cards,
      new Dictionary<string, IBotAgent>(StringComparer.Ordinal)
      {
        ["player-1"] = new OraclePriorityBot(),
        ["player-2"] = new OraclePriorityBot()
      },
      new BotSimulationOptions(OpeningHandSize: 2, MaxCommands: 120, BotSeed: 2202),
      profile,
      new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry()));

    Assert.True(
      simulation.ReplayVerification.IsVerified,
      string.Join(
        Environment.NewLine,
        simulation.ReplayVerification.Diagnostics.Select(diagnostic =>
          $"{diagnostic.Code}:{diagnostic.Path}:{diagnostic.Identifier}:{diagnostic.Message}")));
    Assert.Equal(simulation.FinalStateHash, simulation.ReplayVerification.FinalStateHash);
    Assert.True(simulation.TerminalState.IsTerminal);
    Assert.Equal("HERO_DEFEATED", simulation.TerminalState.Reason);
    Assert.NotNull(simulation.TerminalState.WinningPlayerId);
    Assert.Contains(simulation.DerivedEvents, committedEvent =>
      committedEvent.EventType == CommittedEventType.GameEnded);
    Assert.Contains(simulation.CommandLog.Payloads, payload =>
      payload.CommandType == AttackCommand.CommandKind);
    Assert.Equal(
      Enumerable.Range(1, simulation.CommandLog.Payloads.Count),
      simulation.CommandLog.Payloads.Select(payload => payload.CommandSeq));

    var finalState = Assert.IsType<InMemoryGameState>(simulation.FinalState);
    Assert.Empty(finalState.Players["player-1"].Zones.Deck);
    Assert.True(finalState.Players["player-1"].Zones.Hand.Count<=1);
    Assert.Empty(finalState.Players["player-2"].Zones.Deck);
    Assert.True(finalState.Players["player-2"].Zones.Hand.Count<=1);
    Assert.True(finalState.TerminalState.IsTerminal);
    Assert.Equal("HERO_DEFEATED", finalState.TerminalState.Reason);
    Assert.Contains(simulation.CommandLog.Payloads, payload =>
      payload.CommandType == PlayCardCommand.CommandKind);
    Assert.Contains(simulation.CommandLog.Payloads, payload =>
      payload.CommandType == PlayCardCommand.CommandKind);
    Assert.Contains(simulation.DerivedEvents, committedEvent =>
      committedEvent.EventType == CommittedEventType.EntityDamaged);
    Assert.NotEmpty(simulation.BotDecisionRolls ?? []);
  }

  private static CardVersion GenerateAndPublish(
    ResolvedRulesetProfile profile,
    string generationId,
    string cardId,
    string cardName,
    string cardVersionId,
    int attack,
    int health,
    string catalogSnapshotId = "catalog-bot-e2e",
    bool includeComplexOnPlayEffect = false)
  {
    var context = new GenerationContextBuilder().Build(
      profile,
      [CreateMechanic("BATTLECRY", MechanicStatus.ApprovedForRuleset)]);
    var validationRequest = new GeneratedCardValidationRequest(
      generationId,
      "pipeline-v1",
      "context-v1",
      GenerateCandidate(
        profile,
        context,
        generationId,
        cardId,
        cardName,
        attack,
        health,
        includeComplexOnPlayEffect),
      context,
      BalanceReport: null,
      SimulationReport: null,
      ReplayReport: null);
    var validationReport = GeneratedCardValidationPipeline.CreateDefault().Validate(validationRequest);
    Assert.True(
      validationReport.FinalDecision is GeneratedCardValidationDecision.Accepted,
      string.Join(
        Environment.NewLine,
        validationReport.StageReports.SelectMany(stage =>
          stage.Diagnostics.Select(diagnostic =>
            $"{stage.Stage}:{diagnostic.Code}:{diagnostic.Path}:{diagnostic.ActualValue}"))));

    var publication = new GeneratedCardPublisher().Publish(new GeneratedCardPublicationRequest(
      validationRequest,
      validationReport,
      cardVersionId,
      catalogSnapshotId,
      FixedNow,
      new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry()));

    Assert.True(publication.Succeeded);
    return Assert.IsType<CardVersion>(publication.CardVersion);
  }

  private static CardDefinition GenerateCandidate(
    ResolvedRulesetProfile profile,
    GenerationContext context,
    string generationId,
    string cardId,
    string cardName,
    int attack,
    int health,
    bool includeComplexOnPlayEffect = false)
  {
    var skeletonResult = new GeneratedCardSkeletonBuilder().Build(new SkeletonGenerationRequest(
      $"skeleton-{generationId}",
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      "primitive-registry-bootstrap-0.2.0",
      cardName,
      CardType.MinionCard,
      ["mage"],
      Rarity.Rare,
      ManaCost: 2,
      ["APPRENTICE"],
      ["BATTLECRY"],
      Collectible: true,
      FormatTags: ["bot-e2e"],
      attack,
      health,
      CanAttack: true,
      RequiresTarget: false,
      "bot-vs-bot-e2e-generator",
      FixedNow,
      context));

    Assert.True(
      skeletonResult.Succeeded,
      string.Join(
        Environment.NewLine,
        skeletonResult.Diagnostics.Select(diagnostic =>
          $"{diagnostic.Code}:{diagnostic.Path}:{diagnostic.ActualValue}")));
    var skeleton = Assert.IsType<GeneratedCardSkeleton>(skeletonResult.Skeleton);
    var candidate = new GeneratedCardSkeletonCompiler().Compile(skeleton);

    candidate = candidate with
    {
      CardId = cardId,
      RulesetProfileId = profile.SourceProfileId,
      AuthoringMetadata = candidate.AuthoringMetadata with
      {
        SourceRefs =
        [
          ..candidate.AuthoringMetadata.SourceRefs,
          "generator:skeleton-builder",
          "generator:skeleton-compiler"
        ]
      }
    };

    if (!includeComplexOnPlayEffect)
    {
      return candidate;
    }

    var expansion = new GeneratedCardEffectExpander().Expand(new GeneratedCardEffectExpansionRequest(
      $"effect-expansion-{generationId}",
      candidate,
      context,
      CreateComplexOnPlayEffect(generationId),
      "Battlecry: Deal 1 damage to your first minion. Draw a card.",
      ComplexityScore: 2,
      RandomnessScore: 0,
      RiskFlags: []));

    Assert.True(
      expansion.Succeeded,
      string.Join(
        Environment.NewLine,
        expansion.Diagnostics.Select(diagnostic =>
          $"{diagnostic.Code}:{diagnostic.Path}:{diagnostic.ActualValue}")));

    return Assert.IsType<CardDefinition>(expansion.Candidate);
  }

  private static EffectDefinition CreateComplexOnPlayEffect(string generationId) =>
    new(
      $"effect-{generationId}-on-play-damage-draw",
      EffectKind.SimplePlayEffect,
      [SourceZone.Hand],
      Trigger: null,
      new TimingPolicy(Phase.OnPlay, Window: null, Priority: 0, OrderingPolicy: "DECLARED_ORDER"),
      Conditions: [],
      TargetRequirement: new TargetSpec(
        Required: true,
        TargetSelectionMode.AutoValid,
        new EntityQuery(
          Controller: "SELF",
          [SourceZone.Board],
          [EntityType.Minion],
          RequiredTags: [],
          ExcludedTags: [],
          AliveOnly: true),
        new CountSpec(Min: 1, Max: 1),
        RetargetPolicy: "FIZZLE"),
      Actions:
      [
        new DealDamageActionDefinition(
          $"action-{generationId}-deal-1",
          new ValueExpression("FIXED", FixedValue: 1, Ref: null),
          new TargetRef("TARGET"),
          DamageSchool: null,
          Lifesteal: false,
          Preventable: true),
        new DrawCardActionDefinition(
          $"action-{generationId}-draw-1",
          PlayerSelector.Self,
          new ValueExpression("FIXED", FixedValue: 1, Ref: null),
          DeckEmptyPolicy: "FIZZLE",
          ResultRef: null)
      ],
      new ResolutionPolicy("AUTO_VALID"),
      new FizzlePolicy("FIZZLE_WHOLE_EFFECT"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["BATTLECRY"]);

  private static GeneratedCardSimulationEvidenceReport BuildEvidenceReport(
    BotSimulationResult simulation,
    IReadOnlyList<string> scenarioSets,
    IReadOnlyList<string> edgeCases,
    int randomBotGames,
    int baselineDeckGames)
  {
    var diagnostics = simulation.ReplayVerification.Diagnostics
      .Select(diagnostic =>
        $"{diagnostic.Code}:{diagnostic.Path}:{diagnostic.Identifier}:{diagnostic.Message}")
      .ToArray();
    var matchEvidence = new GeneratedCardSimulationMatchEvidence(
      "generated-deck-bot-vs-bot",
      simulation.SimulationId,
      $"simulation-seed:{simulation.SimulationId}",
      $"command-log:{simulation.SimulationId}:{simulation.CommandLog.Payloads.Count}",
      simulation.InitialStateHash,
      simulation.SetupStateHash,
      simulation.FinalStateHash,
      simulation.CommandLog.Payloads.Count,
      simulation.DerivedEvents.Count,
      simulation.FinalState.TurnNumber,
      simulation.TerminalState.Reason,
      simulation.TerminalState.WinningPlayerId,
      simulation.ReplayVerification.IsVerified,
      simulation.ReplayVerification.FinalStateHash,
      simulation.ReplayVerificationRuns,
      diagnostics);

    return new GeneratedCardSimulationEvidenceBuilder().Build(
      new GeneratedCardSimulationEvidenceRequest(
        $"simulation-report:{simulation.SimulationId}",
        $"replay-report:{simulation.SimulationId}",
        "simulation_production_evidence",
        "publication_mvp",
        scenarioSets,
        edgeCases,
        randomBotGames,
        baselineDeckGames,
        [matchEvidence]));
  }

  private static AssetManifest[] CreateAcceptanceAssetManifests(CardVersion card) =>
  [
    CreateAcceptanceAssetManifest(card, VisualAssetMode.StaticImage, "png"),
    CreateAcceptanceAssetManifest(card, VisualAssetMode.IdleAnimation, "gif")
  ];

  private static AssetManifest CreateAcceptanceAssetManifest(
    CardVersion card,
    VisualAssetMode mode,
    string format)
  {
    var modeId = mode.ToString();
    return new AssetManifest(
      $"asset:{card.CardVersionId}:{modeId}",
      "asset-manifest-bootstrap-0.1.0",
      card.CardId,
      card.CardVersionId,
      mode,
      AudioAssetMode.None,
      new VisualAssetManifest(
        $"visual:{card.CardVersionId}:{modeId}",
        "visual-generated-v1",
        $"asset://{card.CardVersionId}/{modeId}",
        format,
        AssetModerationStatus.Passed,
        $"sha256:visual:{card.CardVersionId}:{modeId}"),
      AudioManifest: null,
      new AssetFallbackPolicy(AllowSilence: true, []),
      AssetModerationStatus.Passed,
      FixedNow,
      $"sha256:asset:{card.CardVersionId}:{modeId}");
  }

  private static Game.Generator.Audit.GenerationAuditLogEntry CreateAcceptanceAuditEntry(CardVersion card) =>
    new(
      $"generation:{card.CardVersionId}",
      FixedNow,
      "pipeline-v1",
      "context-v1",
      card.RulesetProfileId,
      card.RulesetProfileHash,
      $"intent:{card.CardVersionId}",
      $"raw:{card.CardVersionId}",
      $"skeleton:{card.CardVersionId}",
      EffectSlotsRef: null,
      ExpandedEffectsRef: card.Definition.Effects.Count == 0 ? null : $"effects:{card.CardVersionId}",
      ValidationResults: [],
      RepairAttemptRefs: [],
      BalanceReportRef: null,
      SimulationReportRef: "simulation-report:generated-deck-bot-vs-bot",
      ReplayReportRef: "replay-report:generated-deck-bot-vs-bot",
      ImageAssetManifestRef: $"asset:{card.CardVersionId}:StaticImage",
      AnimationAssetManifestRef: $"asset:{card.CardVersionId}:IdleAnimation",
      ProviderProvenanceRefs: [$"test-provider:{card.CardVersionId}:blocked-real-provider-evidence"],
      PublicationRef: card.CardVersionId,
      GeneratedCardValidationDecision.Accepted);

  private static RealProviderGenerationHarnessResult CreateBlockedProviderHarnessResult()
  {
    var missingProvider = new GenerationProviderConfiguration(
      "real-provider",
      "model-unconfigured",
      Endpoint: null,
      ApiKey: null,
      TimeSpan.FromSeconds(60),
      MaxRetryAttempts: 1);
    return new RealProviderGenerationHarnessResult(
      ProductionCardGenerationStatus.Blocked,
      GenerationResult: null,
      [
        new ProductionCardGenerationDiagnostic(
          "PROVIDER_CONFIGURATION_MISSING",
          "providers.structured.endpoint",
          "structured",
          "Real provider endpoint is not configured in local test environment.")
      ],
      [missingProvider.Redacted()]);
  }

  private static IntegrationServices CreateApplicationServices(
    ResolvedRulesetProfile profile,
    CatalogSnapshot catalog)
  {
    var catalogStore = new InMemoryCatalogSnapshotStore();
    catalogStore.Store(catalog);

    var deckStore = new InMemoryDeckStore();
    var profileStore = new InMemoryResolvedRulesetProfileStore();
    profileStore.Store(profile);

    var deckQuery = new DeckQueryService(deckStore);
    var catalogQuery = new CatalogSnapshotQueryService(catalogStore);
    var capabilityValidator = new EngineCapabilityCompatibilityValidator();
    var readiness = new MatchReadinessService(
      deckQuery,
      catalogQuery,
      new ResolvedRulesetProfileQueryService(profileStore),
      new DefaultEngineCapabilityRegistryProvider(),
      new DeckValidator(new CatalogCompatibilityValidator(capabilityValidator)),
      capabilityValidator,
      new Game.Application.Rulesets.DefaultRulesetDefinitionRegistry());

    return new IntegrationServices(
      deckStore,
      readiness,
      new MatchStartService(deckQuery, catalogQuery, readiness));
  }

  private static DeckDefinition CreateDeck(
    string deckId,
    string playerId,
    CatalogSnapshot catalog) =>
    new(
      deckId,
      playerId,
      catalog.SnapshotId,
      catalog.RulesetProfileId,
      catalog.Cards
        .OrderBy(card => card.CardId, StringComparer.Ordinal)
        .Select(card => new DeckCardRef(card.CardId, 1))
        .ToArray());

  private static MechanicStatusRecord CreateMechanic(
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

  private static ResolvedRulesetProfile CreateProfile() =>
    new(
      "ruleset_mvp_core_v1",
      "ruleset_mvp_core_v1",
      "1.0.0",
      FixedNow,
      "bot-vs-bot-e2e",
      "resolved-hash",
      "mvp_internal",
      "core_generated_alpha",
      ["BATTLECRY"],
      [],
      [],
      ["SIMPLE_PLAY_EFFECT"],
      ["ATTACK", "CONCEDE", "DEAL_DAMAGE", "DRAW_CARD", "END_TURN", "PLAY_CARD"],
      ["BATTLECRY"],
      ["HAND", "BOARD"],
      ["ON_PLAY"],
      ["FIXED_POOL"],
      ["MINION_CARD"],
      ["MINION"],
      ["RARE"],
      ["mage"],
      ["APPRENTICE"],
      new RulesetLimits(30, 25, 20, 80, 5, 1, "LIMITED", 3, 2, 3, 3, 3, 2, 2, 3, 1),
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
        RequireBalanceReport: false,
        RequireHumanReviewAboveRisk: "MEDIUM",
        [],
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
        [],
        [],
        0,
        0,
        RequireReplayHashConsistency: false,
        10,
        40),
      new PublicationPolicyRef(
        "publication_mvp",
        RequireValidationPass: true,
        RequireSimulationPass: false,
        RequireReplayPass: false,
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

  private sealed record IntegrationServices(
    InMemoryDeckStore DeckStore,
    MatchReadinessService Readiness,
    MatchStartService MatchStart);
}

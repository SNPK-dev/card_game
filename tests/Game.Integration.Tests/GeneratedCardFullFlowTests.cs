using Game.Application.Capabilities;
using Game.Application.Catalog;
using Game.Application.Decks;
using Game.Application.Execution;
using Game.Application.Matchmaking;
using Game.Application.Presentation;
using Game.Application.Replay;
using Game.Application.Rulesets;
using Game.Domain.Actions;
using Game.Domain.Commands;
using Game.Domain.Events;
using Game.Domain.Hashing;
using Game.Domain.Mutation;
using Game.Domain.Replay;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Generator.Generation;
using Game.Generator.Publication;
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

public sealed class GeneratedCardFullFlowTests
{
  private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-06-25T00:00:00Z");

  private readonly IStateHasher _hasher = new StateHasher(new CanonicalJsonSerializer());

  [Fact]
  public void PublishedGeneratedCardCanStartMatchReplayPlayCardAndBuildPresentationTimeline()
  {
    var profile = CreateProfile();
    var validationRequest = CreateValidationRequest(profile);
    var validationReport = GeneratedCardValidationPipeline.CreateDefault().Validate(validationRequest);
    Assert.Equal(GeneratedCardValidationDecision.Accepted, validationReport.FinalDecision);

    var publication = new GeneratedCardPublisher().Publish(new GeneratedCardPublicationRequest(
      validationRequest,
      validationReport,
      "card_arcane_apprentice_v1",
      "catalog-alpha",
      FixedNow,
      new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry()));
    Assert.True(publication.Succeeded);

    var cardVersion = Assert.IsType<CardVersion>(publication.CardVersion);
    var catalog = Assert.IsType<CatalogSnapshot>(publication.CatalogSnapshot);
    var services = CreateApplicationServices(profile, catalog);
    services.DeckStore.Store(CreateDeck("deck-player-1", "player-1", catalog));
    services.DeckStore.Store(CreateDeck("deck-player-2", "player-2", catalog));

    Assert.True(services.Readiness.Check(new MatchReadinessRequest("deck-player-1")).IsReady);
    Assert.True(services.Readiness.Check(new MatchReadinessRequest("deck-player-2")).IsReady);

    var match = services.MatchStart.Start(new MatchStartRequest(
      "game-generated-full-flow",
      ["deck-player-1", "deck-player-2"],
      "player-1"));
    Assert.True(match.Succeeded);

    var startedState = Assert.IsType<InMemoryGameState>(match.State);
    var cardInstanceId = "player-1:card_arcane_apprentice:001";
    var target=new EntityInstance("enemy-battlecry-target","enemy-card","player-2",EntityType.Minion,1,4,0);
    var handState = startedState.WithPlayer(startedState.Players["player-1"] with { Mana = 10, MaxMana = 10 })
      .WithPlayer(startedState.Players["player-2"] with{Zones=new PlayerZones(startedState.Players["player-2"].Zones.Deck,startedState.Players["player-2"].Zones.Hand,startedState.Players["player-2"].Zones.Graveyard,startedState.Players["player-2"].Zones.SecretZone,startedState.Players["player-2"].Zones.SetAside,[target])});
    Assert.Contains(handState.Players["player-1"].Zones.Hand, card => card.CardInstanceId == cardInstanceId);
    var playCommand = new PlayCardCommand("player-1", cardInstanceId, "entity-player-1-apprentice", 2, 2, 2,TargetPlayerId:"player-2",TargetEntityInstanceId:target.EntityInstanceId);
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        playCommand.PlayerId,
        PlayCardCommand.CommandKind,
        playCommand.Parameters,
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = playCommand.CardInstanceId,
          ["entityInstanceId"] = playCommand.EntityInstanceId,
          ["targetPlayerId"] = playCommand.TargetPlayerId,
          ["targetEntityInstanceId"] = playCommand.TargetEntityInstanceId
        },
        _hasher.HashGameplayState(handState))
    ]);
    var replayer = CreateReplayer(catalog.Cards);
    var expectedReplay = replayer.Replay(handState, commandLog);
    var expectedFinalHash = _hasher.HashGameplayState(expectedReplay.State);

    var replay = new ReplayVerificationService(replayer, _hasher).Verify(new ReplayVerificationRequest(
      handState,
      commandLog,
      expectedFinalHash,
      _hasher.HashGameplayState(handState)));

    Assert.True(replay.IsVerified);
    Assert.Equal(expectedFinalHash, replay.FinalStateHash);
    Assert.Equal(5,replay.DerivedEventCount);
    Assert.Equal(
      [CommittedEventType.ManaChanged, CommittedEventType.CardMovedZone, CommittedEventType.MinionSummoned, CommittedEventType.ModifierApplied,CommittedEventType.EntityDamaged],
      expectedReplay.DerivedEvents.Select(@event => @event.EventType));

    var mutationEvents = ExecutePlayCardBatch(handState, playCommand).CommittedEvents;
    Assert.Equal(
      [CommittedEventType.ManaChanged, CommittedEventType.CardMovedZone, CommittedEventType.MinionSummoned, CommittedEventType.ModifierApplied],
      mutationEvents.Select(@event => @event.EventType));
    Assert.Equal(1,expectedReplay.State.Players["player-2"].Zones.Board.Single(entity=>entity.EntityInstanceId==target.EntityInstanceId).Health-expectedReplay.State.Players["player-2"].Zones.Board.Single(entity=>entity.EntityInstanceId==target.EntityInstanceId).Damage);

    var gameplayHashBeforePresentation = _hasher.HashGameplayState(handState);
    var timeline = CreateTimelineBuilder().Build(new PresentationTimelineBuildRequest(
      "timeline-generated-full-flow",
      [CreateCardPlayedEvent(cardVersion)],
      [CreateAssetManifest(cardVersion)]));

    var entry = Assert.Single(timeline.Timeline.Entries);
    Assert.Equal(PresentationTimelineEntryKind.Cue, entry.EntryKind);
    Assert.Equal(cardVersion.CardId, entry.SourceCardId);
    Assert.Equal(cardVersion.CardVersionId, entry.SourceCardVersionId);
    Assert.Equal(gameplayHashBeforePresentation, _hasher.HashGameplayState(handState));
  }

  private static GeneratedCardValidationRequest CreateValidationRequest(ResolvedRulesetProfile profile)
  {
    var context = new GenerationContextBuilder().Build(
      profile,
      [CreateMechanic("BATTLECRY", MechanicStatus.ApprovedForRuleset)]);

    return new GeneratedCardValidationRequest(
      "generation-full-flow-001",
      "pipeline-v1",
      "context-v1",
      CreateCandidate(),
      context,
      new GeneratedCardBalanceReport("balance-report-001", "balance_mvp", 0.01, 8, 0, 0.01, []),
      new GeneratedCardSimulationReport("simulation-report-001", "simulation_mvp", [], [], 10, 10, true, 12, 30),
      new GeneratedCardReplayReport("replay-report-001", "publication_mvp", true, true, 2, "sha256:final-state", ["mvp-smoke"]));
  }

  private static CardDefinition CreateCandidate() =>
    new(
      "card_arcane_apprentice",
      null,
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      "primitive-registry-bootstrap-0.2.0",
      "ruleset_mvp_core_v1",
      null,
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
        new MinionDefinition(2, 2, ["APPRENTICE"], [], CanAttack: true),
        null,
        null,
        null,
        null,
        null),
      [CreateEffect()],
      new CardTextGeneration("Battlecry: Deal 3 damage.", null),
      new BalanceMetadata(8, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata(
        "integration-test",
        FixedNow,
        "pipeline-v1",
        ["openspec/changes/full-flow-completion-bootstrap/proposal.md"]));

  private static EffectDefinition CreateEffect() =>
    new(
      "effect_battlecry_damage",
      EffectKind.SimplePlayEffect,
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
      [new DeckCardRef("card_arcane_apprentice", 1)]);

  private CommandLogReplayer CreateReplayer(IReadOnlyList<CardVersion> cards) =>
    new(new StateMutator(), new DerivedEventLog(), _hasher,cards);

  private MutationBatchExecutionResult ExecutePlayCardBatch(
    IGameState state,
    PlayCardCommand command)
  {
    var batch = new PlayCardActionExecutor().Execute(new ActionExecutionContext(
      state,
      command,
      state.Revision + 1,
      _hasher.HashGameplayState(state)));

    return new DeterministicMutationBatchExecutor(
      new StateMutator(),
      new CommittedEventFactory()).Execute(state, batch);
  }

  private static PresentationTimelineBuilder CreateTimelineBuilder() =>
    new(new PresentationCueResolver(), new PresentationCatalogHasher(), new PresentationTimelineHasher());

  private static CommittedEventRecord CreateCardPlayedEvent(CardVersion cardVersion) =>
    new(
      1,
      null,
      2,
      CommittedEventType.CardPlayed,
      EventCategory.Committed,
      Phase.OnPlay,
      [new EventRef("CARD", cardVersion.CardId, "HAND", "player-1")],
      [],
      [
        new EventPayloadEntry("cardId", new EventPayloadValue("STRING", cardVersion.CardId, null, null, [])),
        new EventPayloadEntry("cardVersionId", new EventPayloadValue("STRING", cardVersion.CardVersionId, null, null, []))
      ],
      "batch:2:play-card",
      ["mutation:2:play-card"],
      [],
      "sha256:pre",
      "sha256:post",
      EventVisibility.Public);

  private static AssetManifest CreateAssetManifest(CardVersion cardVersion) =>
    new(
      "asset-card-arcane-apprentice-v1",
      "asset-manifest-bootstrap-0.1.0",
      cardVersion.CardId,
      cardVersion.CardVersionId,
      VisualAssetMode.StaticImage,
      AudioAssetMode.SfxOnly,
      new VisualAssetManifest(
        "visual-card-arcane-apprentice-v1",
        "visual-bootstrap-0.1.0",
        "asset://visual/card_arcane_apprentice_v1.png",
        "png",
        AssetModerationStatus.Passed,
        "sha256:visual-card-arcane-apprentice-v1"),
      new AudioAssetManifest(
        "audio-card-arcane-apprentice-v1",
        "audio-bootstrap-0.1.0",
        cardVersion.CardId,
        cardVersion.CardVersionId,
        AudioAssetMode.SfxOnly,
        [
          new AudioCueDefinition(
            "cue-card-arcane-apprentice-on-play",
            AudioCueType.OnPlay,
            "asset://audio/card_arcane_apprentice_on_play.ogg",
            900,
            null,
            null,
            "cards",
            new PlaybackPolicy(true, "DO_NOT_INTERRUPT", "SKIP_IF_BUSY", 10, 1, true),
            new CooldownPolicy(500, "CARD_INSTANCE"),
            null,
            "sha256:audio-card-arcane-apprentice-on-play")
        ],
        [],
        "none",
        "cards",
        AssetModerationStatus.Passed,
        "sha256:audio-card-arcane-apprentice-v1"),
      new AssetFallbackPolicy(true, []),
      AssetModerationStatus.Passed,
      FixedNow,
      "sha256:asset-card-arcane-apprentice-v1");

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
      "integration-test",
      "resolved-hash",
      "mvp_internal",
      "core_generated_alpha",
      ["BATTLECRY"],
      [],
      [],
      ["SIMPLE_PLAY_EFFECT"],
      ["DEAL_DAMAGE"],
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
        30,
        80),
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

using System.Text;
using Game.Domain.Commands;
using Game.Domain.Serialization;
using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Engine;
using Game.Shared.Events;
using Game.Shared.Mutation;
using Game.Shared.Primitives;
using Game.Shared.Ruleset;
using Xunit;

namespace Game.Domain.Tests;

public sealed class CanonicalArtifactSnapshotTests
{
  private readonly ICanonicalSerializer serializer = new CanonicalJsonSerializer();

  [Theory]
  [MemberData(nameof(Snapshots))]
  public void CanonicalArtifactsMatchReviewedSnapshots(string name, object artifact, string expectedJson)
  {
    var json = Encoding.UTF8.GetString(serializer.SerializeToCanonicalBytes(artifact));

    Assert.True(string.Equals(expectedJson, json, StringComparison.Ordinal), $"{name}: {json}");
  }

  public static TheoryData<string, object, string> Snapshots()
  {
    var mutation = CreateMutation();

    return new TheoryData<string, object, string>
    {
      { "deterministic-command-payload", CreateCommandPayload(), """{"CommandCategory":0,"CommandSeq":7,"CommandType":"PLAY_CARD","DebugReplayFlag":false,"ExpectedPreStateHash":"sha256:pre","Payload":{"attack":2,"health":3},"PlayerId":"player-1","Refs":{"cardInstanceId":"card-arcane-apprentice-1","entityInstanceId":"entity-arcane-apprentice-1"}}""" },
      { "proposed-mutation-batch", CreateProposedMutationBatch(mutation), """{"BatchId":"batch-1","CommandSeq":7,"FizzlePolicyRef":"fizzle:whole-effect","Mutations":[{"Category":"STAT_MUTATION","CausedByCommandSeq":7,"CausedByEventSeq":null,"Metadata":[{"Key":"source","Value":"snapshot-test"}],"MutationId":"mutation-1","MutationType":"APPLY_DAMAGE","OrderingKey":"0001","Payload":[{"Key":"amount","Value":{"BoolValue":null,"IntValue":3,"StringValue":null,"StringValues":[],"ValueKind":"INT"}}],"Phase":"ON_PLAY","SourceRef":"card-arcane-apprentice-1","TargetRef":"entity-target-1"}],"ParentEventSeq":null,"Phase":"ON_PLAY","PreStateHash":"sha256:pre","Reason":"PLAY_CARD","ReplacementPolicyRef":"replacement:none","SourceRef":"card-arcane-apprentice-1"}""" },
      { "applied-mutation-batch", CreateAppliedMutationBatch(mutation), """{"AppliedAtStep":1,"AppliedMutations":[{"Category":"STAT_MUTATION","CausedByCommandSeq":7,"CausedByEventSeq":null,"Metadata":[{"Key":"source","Value":"snapshot-test"}],"MutationId":"mutation-1","MutationType":"APPLY_DAMAGE","OrderingKey":"0001","Payload":[{"Key":"amount","Value":{"BoolValue":null,"IntValue":3,"StringValue":null,"StringValues":[],"ValueKind":"INT"}}],"Phase":"ON_PLAY","SourceRef":"card-arcane-apprentice-1","TargetRef":"entity-target-1"}],"BatchId":"batch-1","CommandSeq":7,"DerivedEventRefs":["event-1"],"ParentEventSeq":null,"Phase":"ON_PLAY","PostStateHash":"sha256:post","PreStateHash":"sha256:pre","Reason":"PLAY_CARD","SkippedMutations":[],"SourceRef":"card-arcane-apprentice-1"}""" },
      { "committed-event", CreateCommittedEvent(), """{"Category":"COMMITTED","CommandSeq":7,"EventSeq":11,"EventType":"CARD_PLAYED","MutationBatchRef":"batch-1","MutationRefs":["mutation-1"],"ParentEventSeq":null,"Payload":[{"Key":"cardId","Value":{"BoolValue":null,"IntValue":null,"StringValue":"card_arcane_apprentice","StringValues":[],"ValueKind":"STRING"}}],"Phase":"ON_PLAY","PostStateHash":"sha256:post","PreStateHash":"sha256:pre","RngRollRefs":[],"SourceRefs":[{"ControllerPlayerId":"player-1","RefId":"card-arcane-apprentice-1","RefType":"CARD_INSTANCE","Zone":"HAND"}],"TargetRefs":[{"ControllerPlayerId":"player-1","RefId":"entity-arcane-apprentice-1","RefType":"ENTITY_INSTANCE","Zone":"BOARD"}],"Visibility":"PUBLIC"}""" },
      { "catalog-snapshot", CreateCatalogSnapshot(), """{"Cards":[{"CardId":"card_arcane_apprentice","CardVersionId":"card_arcane_apprentice_v1","CatalogSnapshotId":"catalog-alpha","Definition":{"AssetMetadata":{"AssetManifestId":"asset-alpha","AudioManifestId":"audio-alpha","VisualAssetRef":"visual/card_arcane_apprentice"},"AuthoringMetadata":{"CreatedAt":"2026-06-25T00:00:00\u002B00:00","CreatedBy":"snapshot-test","GenerationPipelineVersion":"pipeline-v1","SourceRefs":["docs/generator/dsl_reference.md"]},"BalanceMetadata":{"ComplexityScore":8,"RandomnessScore":0,"RiskFlags":[]},"CardId":"card_arcane_apprentice","CardText":{"FlavorText":null,"RulesText":"Battlecry: Deal 3 damage."},"CardVersion":"card_arcane_apprentice_v1","CatalogSnapshotId":"catalog-alpha","Effects":[],"EngineVersion":"engine-v1","Entity":{"HeroCard":null,"HeroPower":null,"Location":null,"Minion":{"Attack":2,"BaseKeywords":[],"CanAttack":true,"Health":2,"Tribes":["APPRENTICE"]},"Spell":null,"Weapon":null},"PrimitiveRegistryVersion":"primitive-v1","RulesetProfileId":"ruleset_mvp_core_v1","SchemaVersion":"dsl-v1","StaticData":{"CardType":"MINION_CARD","ClassTags":["mage"],"Collectible":true,"FormatTags":["mvp"],"Keywords":["BATTLECRY"],"ManaCost":2,"Name":"Arcane Apprentice","Rarity":"RARE","Tribes":["APPRENTICE"]}},"EngineVersion":"engine-v1","GenerationPipelineVersion":"pipeline-v1","PublishedAt":"2026-06-25T00:00:00\u002B00:00","RulesetProfileHash":"sha256:resolved","RulesetProfileId":"ruleset_mvp_core_v1","SchemaVersion":"dsl-v1"}],"CreatedAt":"2026-06-25T00:00:00\u002B00:00","RulesetProfileHash":"sha256:resolved","RulesetProfileId":"ruleset_mvp_core_v1","SnapshotId":"catalog-alpha"}""" },
      { "resolved-ruleset-profile", CreateResolvedRulesetProfile(), """{"AllowedActions":["DEAL_DAMAGE"],"AllowedCardTypes":["MINION_CARD"],"AllowedClasses":["MAGE"],"AllowedEffectKinds":["SIMPLE_PLAY_EFFECT"],"AllowedEntityTypes":["MINION"],"AllowedKeywords":[],"AllowedPhases":["ON_PLAY"],"AllowedPools":[],"AllowedRarities":["RARE"],"AllowedSourceZones":["HAND"],"AllowedTribes":["APPRENTICE"],"BalancePolicy":{"AutoRejectRiskFlags":[],"BaselineDeckSetId":"baseline_mvp","ConfidenceInterval":0.95,"MaxComplexityScore":30,"MaxEventCountDelta":5,"MaxRandomnessSwingScore":25,"MaxSimulationWinrateDelta":0.05,"MinSimulationSampleSize":100,"OutlierHandlingPolicy":"flag_outliers","PolicyId":"balance_mvp","RequireBalanceReport":true,"RequireHumanReviewAboveRisk":"MEDIUM","SeedSetId":"seed_mvp"},"CardSetId":"core_generated_alpha","DisabledMechanics":["QUESTLINE"],"EnabledMechanics":["BATTLECRY"],"FormatId":"mvp_internal","FormatMechanicStates":[{"EffectiveFrom":null,"EffectiveUntil":null,"MechanicId":"BATTLECRY","Reason":null,"RulesetProfileId":"ruleset_mvp_core_v1","State":"ENABLED"}],"Id":"resolved_mvp_core_v1","LegacyPolicy":{"AllowDeprecatedMechanicsForNewCards":false,"AllowDeprecatedMechanicsForReplay":true,"AllowDisabledMechanicsForNewCards":false,"AllowDisabledMechanicsForReplay":true,"ReplayProfilePinningRequired":true},"Limits":{"MaxActionsPerEffect":3,"MaxBoardSize":7,"MaxBoardSummonsPerAction":3,"MaxChoiceCount":3,"MaxComplexityScore":30,"MaxDiscoverOfferCount":3,"MaxDrawPerAction":2,"MaxEffectsPerCard":2,"MaxEventCountPerCard":20,"MaxEventCountPerTurn":80,"MaxGeneratedCardsPerEffect":1,"MaxHandSize":10,"MaxHistoryQueryScope":"LIMITED","MaxManaCostReduction":2,"MaxPermanentCostReduction":1,"MaxRandomnessScore":25,"MaxRepeatCount":5,"MaxTemporaryCostReduction":3},"PublicationPolicy":{"AllowAutoPublish":false,"AllowExperimentalPublication":false,"AllowedPublicationStatuses":["validated"],"PolicyId":"publication_mvp","RequireBalanceReview":true,"RequireHumanApproval":true,"RequireReplayPass":true,"RequireSimulationPass":true,"RequireValidationPass":true},"ResolvedAt":"2026-06-25T00:00:00\u002B00:00","ResolvedBy":"profile-resolver","ResolvedHash":"sha256:resolved","SimulationPolicy":{"MaxEventCount":80,"MaxTurns":30,"MinBaselineDeckGames":10,"MinRandomBotGames":10,"PolicyId":"simulation_mvp","RequireReplayHashConsistency":true,"RequiredEdgeCases":["empty_deck"],"RequiredScenarioSets":["smoke"]},"SourceProfileId":"ruleset_mvp_core_v1","SourceProfileVersion":"0.2.0","ValidationPolicy":{"AllowLegacyDeprecatedMechanics":true,"DeserializationValidation":true,"FailOnDeprecatedMechanic":true,"FailOnDisabledMechanic":true,"FailOnUnknownMechanic":true,"PolicyId":"validation_mvp","RuleValidation":true,"SchemaValidation":true,"SemanticValidation":true,"StaticAnalysis":true}}""" },
      { "engine-capability-registry", CreateEngineCapabilityRegistry(), """{"EngineVersion":"engine-v1","Metadata":{"AuthoredAt":"2026-06-25T00:00:00\u002B00:00","AuthoredBy":"snapshot-test","Notes":["Canonical snapshot fixture."],"ReviewedAt":"2026-06-25T00:00:00\u002B00:00","ReviewedBy":"reviewer","SourceReferences":["docs/engine/main.md","docs/engine/canonical_serialization_spec.md"]},"SupportedActionTypes":["DEAL_DAMAGE","DRAW_CARD","SUMMON"],"SupportedAssetManifestVersions":["asset-manifest-v1"],"SupportedEffectKinds":["SIMPLE_PLAY_EFFECT"],"SupportedFizzlePolicies":["FIZZLE_WHOLE_EFFECT"],"SupportedMechanics":["BATTLECRY"],"SupportedPhases":["ON_PLAY"],"SupportedPrimitiveRegistryVersions":["primitive-v1"],"SupportedResolutionPolicies":["SEQUENTIAL"],"SupportedRulesetProfileVersions":["0.2.0","ruleset_mvp_core_v1"],"SupportedSchemaVersions":["dsl-v1"],"SupportedSourceZones":["HAND"]}""" }
    };
  }

  private static DeterministicCommandPayload CreateCommandPayload() =>
    new(
      7,
      "player-1",
      PlayCardCommand.CommandKind,
      new Dictionary<string, int>
      {
        ["health"] = 3,
        ["attack"] = 2
      },
      new Dictionary<string, string>
      {
        ["entityInstanceId"] = "entity-arcane-apprentice-1",
        ["cardInstanceId"] = "card-arcane-apprentice-1"
      },
      "sha256:pre",
      CommandCategory.Player,
      false);

  private static StateMutationRecord CreateMutation() =>
    new(
      "mutation-1",
      MutationType.ApplyDamage,
      MutationCategory.StatMutation,
      "card-arcane-apprentice-1",
      "entity-target-1",
      [new MutationPayloadEntry("amount", new MutationPayloadValue("INT", null, 3, null, []))],
      Phase.OnPlay,
      7,
      null,
      "0001",
      [new MutationMetadataEntry("source", "snapshot-test")]);

  private static ProposedMutationBatchRecord CreateProposedMutationBatch(StateMutationRecord mutation) =>
    new(
      "batch-1",
      7,
      null,
      Phase.OnPlay,
      "card-arcane-apprentice-1",
      "PLAY_CARD",
      [mutation],
      "sha256:pre",
      "replacement:none",
      "fizzle:whole-effect");

  private static AppliedMutationBatchRecord CreateAppliedMutationBatch(StateMutationRecord mutation) =>
    new(
      "batch-1",
      7,
      null,
      Phase.OnPlay,
      "card-arcane-apprentice-1",
      "PLAY_CARD",
      [mutation],
      [],
      "sha256:pre",
      "sha256:post",
      ["event-1"],
      1);

  private static CommittedEventRecord CreateCommittedEvent() =>
    new(
      11,
      null,
      7,
      CommittedEventType.CardPlayed,
      EventCategory.Committed,
      Phase.OnPlay,
      [new EventRef("CARD_INSTANCE", "card-arcane-apprentice-1", "HAND", "player-1")],
      [new EventRef("ENTITY_INSTANCE", "entity-arcane-apprentice-1", "BOARD", "player-1")],
      [new EventPayloadEntry("cardId", new EventPayloadValue("STRING", "card_arcane_apprentice", null, null, []))],
      "batch-1",
      ["mutation-1"],
      [],
      "sha256:pre",
      "sha256:post",
      EventVisibility.Public);

  private static CatalogSnapshot CreateCatalogSnapshot()
  {
    var definition = CreateCardDefinition();
    var version = new CardVersion(
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      "dsl-v1",
      "engine-v1",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:resolved",
      DateTimeOffset.Parse("2026-06-25T00:00:00Z"),
      definition);

    return new CatalogSnapshot(
      "catalog-alpha",
      "ruleset_mvp_core_v1",
      "sha256:resolved",
      DateTimeOffset.Parse("2026-06-25T00:00:00Z"),
      [version]);
  }

  private static CardDefinition CreateCardDefinition() =>
    new(
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData(
        "Arcane Apprentice",
        CardType.MinionCard,
        ["mage"],
        Rarity.Rare,
        2,
        ["APPRENTICE"],
        ["BATTLECRY"],
        true,
        ["mvp"]),
      new EntityDefinition(
        new MinionDefinition(2, 2, ["APPRENTICE"], [], true),
        null,
        null,
        null,
        null,
        null),
      [],
      new CardTextGeneration("Battlecry: Deal 3 damage.", null),
      new BalanceMetadata(8, 0, []),
      new AssetMetadata("asset-alpha", "visual/card_arcane_apprentice", "audio-alpha"),
      new AuthoringMetadata(
        "snapshot-test",
        DateTimeOffset.Parse("2026-06-25T00:00:00Z"),
        "pipeline-v1",
        ["docs/generator/dsl_reference.md"]));

  private static ResolvedRulesetProfile CreateResolvedRulesetProfile() =>
    new(
      "resolved_mvp_core_v1",
      "ruleset_mvp_core_v1",
      "0.2.0",
      DateTimeOffset.Parse("2026-06-25T00:00:00Z"),
      "profile-resolver",
      "sha256:resolved",
      "mvp_internal",
      "core_generated_alpha",
      ["BATTLECRY"],
      ["QUESTLINE"],
      [new FormatMechanicState("BATTLECRY", "ruleset_mvp_core_v1", FormatMechanicStateKind.Enabled, null, null, null)],
      ["SIMPLE_PLAY_EFFECT"],
      ["DEAL_DAMAGE"],
      [],
      ["HAND"],
      ["ON_PLAY"],
      [],
      ["MINION_CARD"],
      ["MINION"],
      ["RARE"],
      ["MAGE"],
      ["APPRENTICE"],
      CreateLimits(),
      CreateValidationPolicy(),
      CreateBalancePolicy(),
      CreateSimulationPolicy(),
      CreatePublicationPolicy(),
      CreateLegacyPolicy());

  private static EngineCapabilityRegistry CreateEngineCapabilityRegistry() =>
    new(
      "engine-v1",
      ["dsl-v1"],
      ["primitive-v1"],
      ["0.2.0", "ruleset_mvp_core_v1"],
      ["DEAL_DAMAGE", "DRAW_CARD", "SUMMON"],
      ["SIMPLE_PLAY_EFFECT"],
      ["BATTLECRY"],
      ["ON_PLAY"],
      ["HAND"],
      ["SEQUENTIAL"],
      ["FIZZLE_WHOLE_EFFECT"],
      ["asset-manifest-v1"],
      new EngineCapabilityMetadata(
        "snapshot-test",
        DateTimeOffset.Parse("2026-06-25T00:00:00Z"),
        "reviewer",
        DateTimeOffset.Parse("2026-06-25T00:00:00Z"),
        ["docs/engine/main.md", "docs/engine/canonical_serialization_spec.md"],
        ["Canonical snapshot fixture."]));

  private static RulesetLimits CreateLimits() =>
    new(30, 25, 20, 80, 5, 1, "LIMITED", 3, 2, 3, 3, 3, 2, 2, 3, 1);

  private static ValidationPolicyRef CreateValidationPolicy() =>
    new("validation_mvp", true, true, true, true, true, true, true, true, true);

  private static BalancePolicyRef CreateBalancePolicy() =>
    new("balance_mvp", true, "MEDIUM", [], 0.05, 30, 25, 5, 100, 0.95, "baseline_mvp", "seed_mvp", "flag_outliers");

  private static SimulationPolicyRef CreateSimulationPolicy() =>
    new("simulation_mvp", ["smoke"], ["empty_deck"], 10, 10, true, 30, 80);

  private static PublicationPolicyRef CreatePublicationPolicy() =>
    new("publication_mvp", true, true, true, true, true, false, false, ["validated"]);

  private static LegacyPolicy CreateLegacyPolicy() =>
    new(true, false, true, false, true);
}

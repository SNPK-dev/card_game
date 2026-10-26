using Game.Application.Capabilities;
using Game.Application.Catalog;
using Game.Application.Decks;
using Game.Application.Rulesets;
using Game.Shared.Catalog;
using Game.Shared.Decks;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Game.Shared.Ruleset;
using Xunit;

namespace Game.Application.Tests;

public sealed class DeckValidatorTests
{
  private const string ProductionRulesetId = DefaultRulesetDefinitionRegistry.ProductionRulesetId;
  private const string ProductionRulesetVersion = DefaultRulesetDefinitionRegistry.ProductionRulesetVersion;

  [Fact]
  public void ValidateAcceptsDeckPinnedToCompatibleCatalogAndResolvedProfile()
  {
    var snapshot = CreateSnapshot([CreateSupportedCard("card_arcane_apprentice")]);
    var deck = new DeckDefinition(
      "deck-alpha",
      "player-1",
      snapshot.SnapshotId,
      snapshot.RulesetProfileId,
      [new DeckCardRef("card_arcane_apprentice", 2)]);
    var validator = CreateValidator();

    var result = validator.Validate(new DeckValidationRequest(
      deck,
      snapshot,
      CreateProfile(),
      new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry()));

    Assert.True(result.IsValid);
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public void ValidateRejectsMismatchedPinsInvalidCardRefsAndUnsupportedCatalogCards()
  {
    var unsupportedCard = CreateSupportedCard("card_arcane_apprentice") with
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
    var snapshot = CreateSnapshot([unsupportedCard]);
    var deck = new DeckDefinition(
      "deck-alpha",
      "player-1",
      "other-catalog",
      "other-ruleset",
      [
        new DeckCardRef("card_arcane_apprentice", 0),
        new DeckCardRef("card_arcane_apprentice", 1),
        new DeckCardRef("card_missing", 1)
      ]);
    var validator = CreateValidator();

    var result = validator.Validate(new DeckValidationRequest(
      deck,
      snapshot,
      CreateProfile(),
      new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry()));

    Assert.False(result.IsValid);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "DECK_CATALOG_SNAPSHOT_MISMATCH"
      && diagnostic.Path == "deck.catalogSnapshotId");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "DECK_CATALOG_RULESET_PROFILE_MISMATCH"
      && diagnostic.Path == "deck.rulesetProfileId");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "DECK_RESOLVED_RULESET_PROFILE_MISMATCH"
      && diagnostic.Path == "deck.rulesetProfileId");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "DECK_CARD_COUNT_INVALID"
      && diagnostic.Path == "deck.cards[0].count");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "DECK_CARD_DUPLICATE"
      && diagnostic.Path == "deck.cards[1].cardId");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "DECK_CARD_NOT_IN_CATALOG"
      && diagnostic.Path == "deck.cards[2].cardId");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "UNSUPPORTED_ACTION"
      && diagnostic.Path == "catalog.snapshot.cards[0].definition.effects[0].actions[0]");
  }

  [Fact]
  public void ValidateRejectsResolvedHashMismatch()
  {
    var snapshot = CreateSnapshot([CreateSupportedCard("card_arcane_apprentice")]) with
    {
      RulesetProfileHash = "stale-hash"
    };
    var deck = new DeckDefinition(
      "deck-alpha",
      "player-1",
      snapshot.SnapshotId,
      snapshot.RulesetProfileId,
      [new DeckCardRef("card_arcane_apprentice", 1)]);
    var validator = CreateValidator();

    var result = validator.Validate(new DeckValidationRequest(
      deck,
      snapshot,
      CreateProfile(),
      new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry()));

    Assert.False(result.IsValid);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "DECK_RESOLVED_RULESET_HASH_MISMATCH"
      && diagnostic.Path == "catalogSnapshot.rulesetProfileHash");
  }

  [Fact]
  public void ValidateAcceptsProductionDeckAtExactSizeWithPublishedVersions()
  {
    var snapshot = CreateProductionSnapshot(CreateProductionCards(30));
    var deck = CreateProductionDeck(snapshot, snapshot.Cards.Select(card => new DeckCardRef(card.CardId, 1, card.CardVersionId)).ToArray());
    var validator = CreateValidator();

    var result = validator.Validate(CreateProductionRequest(deck, snapshot));

    Assert.True(result.IsValid);
    Assert.Equal(30, result.ActualDeckSize);
    Assert.Equal(30, result.ExpectedDeckSize);
    Assert.Equal(ProductionRulesetId, result.RulesetId);
    Assert.Equal(ProductionRulesetVersion, result.RulesetVersion);
  }

  [Theory]
  [InlineData(29, "DECK_TOO_SMALL")]
  [InlineData(31, "DECK_TOO_LARGE")]
  public void ValidateRejectsProductionDeckOutsideExactSize(int deckSize, string code)
  {
    var snapshot = CreateProductionSnapshot(CreateProductionCards(31));
    var deck = CreateProductionDeck(snapshot, snapshot.Cards.Take(deckSize).Select(card => new DeckCardRef(card.CardId, 1, card.CardVersionId)).ToArray());

    var result = CreateValidator().Validate(CreateProductionRequest(deck, snapshot));

    Assert.False(result.IsValid);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
  }

  [Fact]
  public void ValidateRejectsProductionDeckWhenCopyLimitExceeded()
  {
    var snapshot = CreateProductionSnapshot(CreateProductionCards(28).Append(CreateProductionCardVersion("card_over_limit")).ToArray());
    var cards = snapshot.Cards.Take(28).Select(card => new DeckCardRef(card.CardId, 1, card.CardVersionId))
      .Append(new DeckCardRef("card_over_limit", 3, "card_over_limit_v1"))
      .ToArray();
    var deck = CreateProductionDeck(snapshot, cards);

    var result = CreateValidator().Validate(CreateProductionRequest(deck, snapshot));

    Assert.False(result.IsValid);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CARD_COPY_LIMIT_EXCEEDED");
  }

  [Theory]
  [InlineData("card_missing", "card_missing_v1", "CARD_NOT_FOUND")]
  [InlineData("card_prod_001", "", "CARD_VERSION_NOT_FOUND")]
  [InlineData("card_prod_001", "wrong_version", "CARD_VERSION_NOT_FOUND")]
  public void ValidateRejectsProductionDeckCardLegalityProblems(string cardId, string? cardVersionId, string code)
  {
    var snapshot = CreateProductionSnapshot(CreateProductionCards(30));
    var cards = snapshot.Cards.Skip(1).Select(card => new DeckCardRef(card.CardId, 1, card.CardVersionId))
      .Append(new DeckCardRef(cardId, 1, cardVersionId))
      .ToArray();
    var deck = CreateProductionDeck(snapshot, cards);

    var result = CreateValidator().Validate(CreateProductionRequest(deck, snapshot));

    Assert.False(result.IsValid);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
  }

  [Fact]
  public void ValidateRejectsUnpublishedProductionCardVersion()
  {
    var cards = CreateProductionCards(30).ToArray();
    cards[0] = cards[0] with { Published = false };
    var snapshot = CreateProductionSnapshot(cards);
    var deck = CreateProductionDeck(snapshot, snapshot.Cards.Select(card => new DeckCardRef(card.CardId, 1, card.CardVersionId)).ToArray());

    var result = CreateValidator().Validate(CreateProductionRequest(deck, snapshot));

    Assert.False(result.IsValid);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CARD_NOT_PUBLISHED");
  }

  [Fact]
  public void ValidateRejectsProductionCatalogWithoutCompatibilityDeclaration()
  {
    var snapshot = CreateProductionSnapshot(CreateProductionCards(30)) with
    {
      CompatibleRulesetIds = [],
      CompatibleRulesetVersions = [],
      ProductionScaleCompatible = false
    };
    var deck = CreateProductionDeck(snapshot, snapshot.Cards.Select(card => new DeckCardRef(card.CardId, 1, card.CardVersionId)).ToArray());

    var result = CreateValidator().Validate(CreateProductionRequest(deck, snapshot));

    Assert.False(result.IsValid);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CATALOG_RULESET_MISMATCH");
  }

  [Fact]
  public void ValidateRejectsNonProductionRulesetWhenUsedAsProductionContract()
  {
    var snapshot = CreateProductionSnapshot(CreateProductionCards(30));
    var deck = CreateProductionDeck(snapshot, snapshot.Cards.Select(card => new DeckCardRef(card.CardId, 1, card.CardVersionId)).ToArray());
    var invalidRuleset = CreateProductionRuleset() with
    {
      Environment = RulesetEnvironment.Test,
      ProductionScale = true
    };

    var result = CreateValidator().Validate(new DeckValidationRequest(
      deck,
      snapshot,
      CreateProductionProfile(),
      new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry(),
      RulesetDefinition: invalidRuleset));

    Assert.False(result.IsValid);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "INVALID_RULESET");
  }

  private static DeckValidator CreateValidator() =>
    new(new CatalogCompatibilityValidator(new EngineCapabilityCompatibilityValidator()));

  private static DeckValidationRequest CreateProductionRequest(
    DeckDefinition deck,
    CatalogSnapshot snapshot) =>
    new(
      deck,
      snapshot,
      CreateProductionProfile(),
      new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry(),
      RulesetDefinition: CreateProductionRuleset());

  private static RulesetDefinition CreateProductionRuleset() =>
    new DefaultRulesetDefinitionRegistry().DefaultProductionRuleset;

  private static DeckDefinition CreateProductionDeck(
    CatalogSnapshot snapshot,
    IReadOnlyList<DeckCardRef> cards) =>
    new("deck-production", "player-1", snapshot.SnapshotId, ProductionRulesetId, cards);

  private static CatalogSnapshot CreateProductionSnapshot(IReadOnlyList<CardVersion> cards) =>
    new(
      "catalog-production",
      ProductionRulesetId,
      "resolved-production-hash",
      DateTimeOffset.Parse("2026-07-12T00:00:00Z"),
      cards,
      [ProductionRulesetId],
      [ProductionRulesetVersion],
      ProductionScaleCompatible: true);

  private static IReadOnlyList<CardVersion> CreateProductionCards(int count) =>
    Enumerable.Range(1, count)
      .Select(index => CreateProductionCardVersion($"card_prod_{index:000}"))
      .ToArray();

  private static CardVersion CreateProductionCardVersion(string cardId)
  {
    var card = CreateSupportedCard(cardId) with
    {
      CardVersion = $"{cardId}_v1",
      CatalogSnapshotId = "catalog-production",
      RulesetProfileId = ProductionRulesetId
    };

    return new CardVersion(
      card.CardId ?? throw new InvalidOperationException("Test card id is required."),
      $"{cardId}_v1",
      card.SchemaVersion,
      card.EngineVersion,
      "catalog-production",
      "pipeline-v1",
      ProductionRulesetId,
      "resolved-production-hash",
      DateTimeOffset.Parse("2026-07-12T00:00:00Z"),
      card);
  }

  private static ResolvedRulesetProfile CreateProductionProfile() =>
    CreateProfile() with
    {
      Id = "resolved-ruleset-production-core-v1",
      SourceProfileId = ProductionRulesetId,
      SourceProfileVersion = ProductionRulesetVersion,
      ResolvedHash = "resolved-production-hash",
      FormatId = "production_core"
    };

  private static CatalogSnapshot CreateSnapshot(IReadOnlyList<CardDefinition> cards)
  {
    var cardVersions = cards
      .Select(card => new CardVersion(
        card.CardId ?? throw new InvalidOperationException("Test card id is required."),
        $"{card.CardId}_v1",
        card.SchemaVersion,
        card.EngineVersion,
        "catalog-alpha",
        "pipeline-v1",
        "ruleset_mvp_core_v1",
        "resolved-hash",
        DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
        card))
      .ToArray();

    return new CatalogSnapshot(
      "catalog-alpha",
      "ruleset_mvp_core_v1",
      "resolved-hash",
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      cardVersions);
  }

  private static ResolvedRulesetProfile CreateProfile() =>
    new(
      "resolved-ruleset-mvp-core-v1",
      "ruleset_mvp_core_v1",
      "1.0.0",
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      "test",
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
      new RulesetLimits(
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

  private static CardDefinition CreateSupportedCard(string cardId) =>
    new(
      cardId,
      $"{cardId}_v1",
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      "primitive-registry-bootstrap-0.2.0",
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
        Collectible: true,
        ["mvp"]),
      new EntityDefinition(
        new MinionDefinition(2, 2, ["APPRENTICE"], [], CanAttack: true),
        null,
        null,
        null,
        null,
        null),
      [CreateSupportedEffect()],
      new CardTextGeneration("Battlecry: Deal 3 damage.", null),
      new BalanceMetadata(8, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata(
        "deck-validation-test",
        DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
        "pipeline-v1",
        ["openspec/changes/deck-validation-bootstrap/design.md"]));

  private static EffectDefinition CreateSupportedEffect() =>
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
}

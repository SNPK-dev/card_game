using Game.Generator.Generation;
using Game.Generator.Skeletons;
using Game.Generator.Validation;
using Game.Shared.Mechanics;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Generator.Tests;

public sealed class GeneratedCardSkeletonTests
{
  [Fact]
  public void BuildAcceptsWhitelistedMinionSkeletonAndCompilesEmptyEffectCandidate()
  {
    var context = CreateContext(
      allowedCardTypes: ["MINION_CARD"],
      allowedRarities: ["RARE"],
      allowedClasses: ["mage"],
      allowedTribes: ["APPRENTICE"],
      allowedKeywords: ["BATTLECRY"]);
    var request = CreateRequest(context);

    var result = new GeneratedCardSkeletonBuilder().Build(request);

    Assert.True(result.Succeeded);
    Assert.NotNull(result.Skeleton);
    var skeleton = result.Skeleton;
    Assert.Equal("ruleset_mvp_core_v1", skeleton.RulesetProfileId);
    Assert.Equal("catalog-alpha", skeleton.CatalogSnapshotId);
    Assert.NotNull(skeleton.Minion);
    Assert.Null(skeleton.Spell);

    var candidate = new GeneratedCardSkeletonCompiler().Compile(skeleton);

    Assert.Equal(CardType.MinionCard, candidate.StaticData.CardType);
    Assert.NotNull(candidate.Entity.Minion);
    Assert.Null(candidate.Entity.Spell);
    Assert.Empty(candidate.Effects);
    Assert.Equal(DateTimeOffset.Parse("2026-06-23T12:00:00Z"), candidate.AuthoringMetadata.CreatedAt);
  }

  [Fact]
  public void BuildRejectsSkeletonOutsideWhitelists()
  {
    var context = CreateContext(
      allowedCardTypes: ["MINION_CARD"],
      allowedRarities: ["RARE"],
      allowedClasses: ["mage"],
      allowedTribes: ["APPRENTICE"],
      allowedKeywords: ["BATTLECRY"]);
    var request = CreateRequest(
      context,
      rarity: Rarity.Legendary,
      classTags: ["rogue"],
      tribes: ["PIRATE"],
      keywords: ["COMBO"]);

    var result = new GeneratedCardSkeletonBuilder().Build(request);

    Assert.False(result.Succeeded);
    Assert.Null(result.Skeleton);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == SkeletonGenerationDiagnosticCode.RarityNotAllowed
      && diagnostic.ActualValue == "LEGENDARY");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == SkeletonGenerationDiagnosticCode.ClassTagNotAllowed
      && diagnostic.ActualValue == "rogue");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == SkeletonGenerationDiagnosticCode.TribeNotAllowed
      && diagnostic.ActualValue == "PIRATE");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == SkeletonGenerationDiagnosticCode.KeywordNotAllowed
      && diagnostic.ActualValue == "COMBO");
  }

  [Fact]
  public void BuildCompilesSpellSkeletonWithoutMinionEntity()
  {
    var context = CreateContext(
      allowedCardTypes: ["SPELL_CARD"],
      allowedRarities: ["COMMON"],
      allowedClasses: ["mage"],
      allowedTribes: [],
      allowedKeywords: []);
    var request = CreateRequest(
      context,
      cardType: CardType.SpellCard,
      rarity: Rarity.Common,
      tribes: [],
      keywords: [],
      attack: null,
      health: null,
      requiresTarget: true);

    var result = new GeneratedCardSkeletonBuilder().Build(request);

    Assert.True(result.Succeeded);
    Assert.NotNull(result.Skeleton);
    var candidate = new GeneratedCardSkeletonCompiler().Compile(result.Skeleton);

    Assert.Equal(CardType.SpellCard, candidate.StaticData.CardType);
    Assert.Null(candidate.Entity.Minion);
    Assert.NotNull(candidate.Entity.Spell);
    Assert.True(candidate.Entity.Spell.RequiresTarget);
    Assert.Empty(candidate.Effects);
  }

  [Fact]
  public void CompiledSkeletonPassesValidationPipeline()
  {
    var context = CreateContext(
      allowedCardTypes: ["MINION_CARD"],
      allowedRarities: ["RARE"],
      allowedClasses: ["mage"],
      allowedTribes: ["APPRENTICE"],
      allowedKeywords: ["BATTLECRY"]);
    var buildResult = new GeneratedCardSkeletonBuilder().Build(CreateRequest(context));
    Assert.NotNull(buildResult.Skeleton);
    var skeleton = buildResult.Skeleton;
    var candidate = new GeneratedCardSkeletonCompiler().Compile(skeleton);
    var request = new GeneratedCardValidationRequest(
      "generation-skeleton-001",
      "pipeline-v1",
      "context-v1",
      candidate,
      context,
      TestFixtures.CreateBalanceReport(),
      TestFixtures.CreateSimulationReport());

    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);

    Assert.Equal(GeneratedCardValidationDecision.Accepted, report.FinalDecision);
  }

  private static GenerationContext CreateContext(
    IReadOnlyList<string> allowedCardTypes,
    IReadOnlyList<string> allowedRarities,
    IReadOnlyList<string> allowedClasses,
    IReadOnlyList<string> allowedTribes,
    IReadOnlyList<string> allowedKeywords)
  {
    var profile = TestFixtures.CreateProfile(
      allowedCardTypes: allowedCardTypes,
      allowedRarities: allowedRarities,
      allowedClasses: allowedClasses,
      allowedTribes: allowedTribes,
      allowedKeywords: allowedKeywords);
    var intent = new GenerationIntent(
      "intent-skeleton",
      "catalog-alpha",
      RequestedMechanics: [],
      RequestedActions: [],
      RequestedEffectKinds: [],
      RequestedSourceZones: [],
      RequestedPhases: [],
      RequestedPools: [],
      RequestedCardTypes: [],
      RequestedRarities: [],
      RequestedClasses: [],
      RequestedTribes: [],
      RequestedKeywords: []);

    return new GenerationContextBuilder().Build(
      profile,
      [TestFixtures.Mechanic("BATTLECRY", MechanicStatus.ApprovedForRuleset)],
      intent);
  }

  private static SkeletonGenerationRequest CreateRequest(
    GenerationContext context,
    CardType cardType = CardType.MinionCard,
    Rarity rarity = Rarity.Rare,
    IReadOnlyList<string>? classTags = null,
    IReadOnlyList<string>? tribes = null,
    IReadOnlyList<string>? keywords = null,
    int? attack = 2,
    int? health = 2,
    bool requiresTarget = false) =>
    new(
      "skeleton-001",
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "Arcane Apprentice",
      cardType,
      classTags ?? ["mage"],
      rarity,
      ManaCost: 2,
      tribes ?? ["APPRENTICE"],
      keywords ?? ["BATTLECRY"],
      Collectible: true,
      FormatTags: ["mvp"],
      attack,
      health,
      CanAttack: true,
      requiresTarget,
      "skeleton-test",
      DateTimeOffset.Parse("2026-06-23T12:00:00Z"),
      context);
}

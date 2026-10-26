using Game.Generator.Generation;
using Game.Generator.Validation;
using Game.Shared.Mechanics;
using Xunit;

namespace Game.Generator.Tests;

public sealed class GenerationContextWhitelistTests
{
  [Fact]
  public void BuildNarrowsProfileAllowlistsWithIntentWithoutWidening()
  {
    var profile = TestFixtures.CreateProfile(
      enabledMechanics: ["SUMMON_TOKEN", "BATTLECRY"],
      allowedActions: ["SUMMON", "DEAL_DAMAGE"],
      allowedEffectKinds: ["POOL_BASED_EFFECT", "SIMPLE_PLAY_EFFECT"],
      allowedCardTypes: ["MINION_CARD"],
      allowedRarities: ["RARE", "COMMON"],
      allowedClasses: ["mage", "neutral"],
      allowedTribes: ["APPRENTICE", "ELEMENTAL"],
      allowedKeywords: ["BATTLECRY", "TAUNT"]);
    var intent = new GenerationIntent(
      "intent-001",
      "catalog-alpha",
      RequestedMechanics: ["BATTLECRY", "SMART_OFFER"],
      RequestedActions: ["DEAL_DAMAGE", "DISCOVER"],
      RequestedEffectKinds: ["SIMPLE_PLAY_EFFECT", "BATCH_EFFECT"],
      RequestedSourceZones: ["HAND"],
      RequestedPhases: ["ON_PLAY"],
      RequestedPools: ["FIXED_POOL"],
      RequestedCardTypes: ["SPELL_CARD"],
      RequestedRarities: ["COMMON"],
      RequestedClasses: ["mage"],
      RequestedTribes: ["ELEMENTAL"],
      RequestedKeywords: ["BATTLECRY"]);

    var context = new GenerationContextBuilder().Build(
      profile,
      [
        TestFixtures.Mechanic("BATTLECRY", MechanicStatus.ApprovedForRuleset),
        TestFixtures.Mechanic("SUMMON_TOKEN", MechanicStatus.ApprovedForRuleset)
      ],
      intent);

    Assert.Equal("intent-001", context.IntentId);
    Assert.Equal("catalog-alpha", context.CatalogSnapshotId);
    Assert.Equal(["BATTLECRY"], context.AllowedMechanics.Order(StringComparer.Ordinal));
    Assert.Equal(["DEAL_DAMAGE"], context.AllowedActions.Order(StringComparer.Ordinal));
    Assert.Equal(["SIMPLE_PLAY_EFFECT"], context.AllowedEffectKinds.Order(StringComparer.Ordinal));
    Assert.Empty(context.AllowedCardTypes);
    Assert.Equal(["COMMON"], context.AllowedRarities.Order(StringComparer.Ordinal));
    Assert.Equal(["mage"], context.AllowedClasses.Order(StringComparer.Ordinal));
    Assert.Equal(["ELEMENTAL"], context.AllowedTribes.Order(StringComparer.Ordinal));
    Assert.Equal(["BATTLECRY"], context.AllowedKeywords.Order(StringComparer.Ordinal));
  }

  [Fact]
  public void BuildCreatesStableSortedWhitelistManifests()
  {
    var profile = TestFixtures.CreateProfile(
      enabledMechanics: ["SUMMON_TOKEN", "BATTLECRY"],
      allowedActions: ["SUMMON", "DEAL_DAMAGE"],
      allowedEffectKinds: ["POOL_BASED_EFFECT", "SIMPLE_PLAY_EFFECT"]);

    var context = new GenerationContextBuilder().Build(
      profile,
      [
        TestFixtures.Mechanic("SUMMON_TOKEN", MechanicStatus.ApprovedForRuleset),
        TestFixtures.Mechanic("BATTLECRY", MechanicStatus.ApprovedForRuleset)
      ]);

    var mechanics = context.WhitelistManifests.Single(manifest => manifest.Kind == "mechanics");
    var actions = context.WhitelistManifests.Single(manifest => manifest.Kind == "actions");

    Assert.Equal("generation-context:mechanics", mechanics.ManifestId);
    Assert.Equal(["BATTLECRY", "SUMMON_TOKEN"], mechanics.Values);
    Assert.Equal(["DEAL_DAMAGE", "SUMMON"], actions.Values);
  }

  [Fact]
  public void CandidateValidationRejectsValuesOutsideWhitelists()
  {
    var profile = TestFixtures.CreateProfile(
      allowedClasses: ["mage"],
      allowedTribes: ["APPRENTICE"],
      allowedKeywords: ["BATTLECRY"]);
    var context = new GenerationContextBuilder().Build(
      profile,
      [TestFixtures.Mechanic("BATTLECRY", MechanicStatus.ApprovedForRuleset)]);
    var candidate = TestFixtures.CreateBattlecryCandidate(
      classTags: ["rogue"],
      tribes: ["PIRATE"],
      keywords: ["COMBO"]);

    var result = new GeneratedCardCandidateValidator().Validate(candidate, context);

    Assert.False(result.IsValid);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.ClassTagNotAllowed
      && diagnostic.ActualValue == "rogue");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.TribeNotAllowed
      && diagnostic.ActualValue == "PIRATE");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.KeywordNotAllowed
      && diagnostic.ActualValue == "COMBO");
  }
}

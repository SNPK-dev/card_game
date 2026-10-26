using Game.Generator.Skeletons;
using Game.Generator.Generation;
using Game.Shared.Mechanics;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Generator.Tests;

public sealed class GeneratedHeroCardSkeletonCompilerTests
{
  [Fact]
  public void CompilerProducesPlayableHeroCardWithReplacementPowerAndPortrait()
  {
    var skeleton = new GeneratedCardSkeleton(
      "hero-skeleton", "1", "1", "1", "ruleset", null,
      "Cinder Ascendant", CardType.HeroCard, ["WARRIOR"], Rarity.Legendary, 7,
      [], [], true, ["STANDARD"], null, null, "test", DateTimeOffset.UnixEpoch,
      new HeroCardSkeleton(6, "asset://heroes/cinder", new HeroPowerSkeleton(2, "ACTIVE_PLAYER_MAIN")));

    var card = new GeneratedCardSkeletonCompiler().Compile(skeleton);

    Assert.NotNull(card.Entity.HeroCard);
    Assert.Equal(6, card.Entity.HeroCard.ArmorGain);
    Assert.Equal("asset://heroes/cinder", card.Entity.HeroCard.PortraitAssetRef);
    Assert.Equal(2, card.Entity.HeroCard.HeroPowerReplacement!.Cost);
  }

  [Fact]
  public void BuilderAcceptsValidatedHeroCardInputEndToEnd()
  {
    var profile=TestFixtures.CreateProfile(allowedCardTypes:["HERO_CARD"],allowedRarities:["LEGENDARY"]);
    var context=new GenerationContextBuilder().Build(profile,[TestFixtures.Mechanic("BATTLECRY",MechanicStatus.ApprovedForRuleset)]);
    var result=new GeneratedCardSkeletonBuilder().Build(new SkeletonGenerationRequest(
      "hero-skeleton","1","1","1","Cinder Ascendant",CardType.HeroCard,["mage"],Rarity.Legendary,7,
      [],[],true,["standard"],null,null,false,false,"test",DateTimeOffset.UnixEpoch,context,
      HeroArmorGain:6,HeroPortraitAssetRef:"asset://heroes/cinder",HeroPowerCost:2));

    Assert.True(result.Succeeded,string.Join(";",result.Diagnostics.Select(item=>item.Message)));
    Assert.NotNull(result.Skeleton!.HeroCard);
    Assert.Equal(6,result.Skeleton.HeroCard.ArmorGain);
  }
}

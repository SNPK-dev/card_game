using Game.Application.Players;
using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Application.Tests;

public sealed class StarterHeroProfileTests
{
  [Fact]
  public void RegistrationPersistsSelectedTrustedStarterHero()
  {
    var store = new InMemoryPlayerAccountStore();
    var service = new PlayerAccountService(store, TimeProvider.System);

    var result = service.Register(
      "varg@example.test",
      "ReleaseReady!123",
      "Varg Player",
      StarterHeroProfiles.CinderVanguardId);

    Assert.True(result.Succeeded);
    Assert.Equal(
      StarterHeroProfiles.CinderVanguardId,
      service.FindById(result.PlayerId!)!.Profile.StarterHeroId);
  }

  [Fact]
  public void RegistrationRejectsUnknownStarterHero()
  {
    var service = new PlayerAccountService(new InMemoryPlayerAccountStore(), TimeProvider.System);

    var result = service.Register("bad-hero@example.test", "ReleaseReady!123", "Player", "unknown-hero");

    Assert.False(result.Succeeded);
    Assert.Equal("AUTH_VALIDATION_FAILED", result.ErrorCode);
  }

  [Fact]
  public void StarterHeroesUseTheOtherTrustedProfileForTheirDefaultOpponent()
  {
    Assert.Equal(StarterHeroProfiles.CinderVanguardId,StarterHeroProfiles.OpponentOf(StarterHeroProfiles.AstralWardenId).HeroId);
    Assert.Equal(StarterHeroProfiles.AstralWardenId,StarterHeroProfiles.OpponentOf(StarterHeroProfiles.CinderVanguardId).HeroId);
  }

  [Fact]
  public void StarterHeroPowerCatalogBuildsDistinctDeclarativeEffects()
  {
    var catalog = new CatalogSnapshot(
      "starter-power-catalog",
      "ruleset_mvp_core_v1",
      "sha256:starter-power",
      DateTimeOffset.Parse("2026-08-17T00:00:00Z"),
      []);

    var astra = StarterHeroPowerCatalog.Create(StarterHeroProfiles.All[0], catalog);
    var varg = StarterHeroPowerCatalog.Create(StarterHeroProfiles.All[1], catalog);

    Assert.Equal(CardType.HeroPowerCard, astra.Definition.StaticData.CardType);
    Assert.Equal(2, astra.Definition.Entity.HeroPower!.Cost);
    Assert.Equal("SELF", astra.Definition.Entity.HeroPower.TargetRequirement!.Candidates.Controller);
    Assert.IsType<HealActionDefinition>(astra.Definition.Entity.HeroPower.Effects.Single().Actions.Single());
    Assert.Equal("OPPONENT", varg.Definition.Entity.HeroPower!.TargetRequirement!.Candidates.Controller);
    Assert.IsType<DealDamageActionDefinition>(varg.Definition.Entity.HeroPower.Effects.Single().Actions.Single());
    Assert.NotEqual(astra.CardVersionId, varg.CardVersionId);
  }
}

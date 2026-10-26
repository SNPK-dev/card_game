using Game.Application.Capabilities;
using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Application.Tests;

public sealed class EngineCapabilityAuditServiceTests
{
  [Fact]
  public void AuditReportsRegistryHashAndAcceptsCompatibleCatalog()
  {
    var registryProvider = new DefaultEngineCapabilityRegistryProvider();
    var registry = registryProvider.GetCurrentRegistry();
    var catalog = CreateCatalog(CreateSupportedCard());
    var profile = TestRulesetProfileFactory.CreateProfile("resolved-ruleset-mvp-core-v1");
    var service = new EngineCapabilityAuditService();

    var report = service.Audit(new EngineCapabilityAuditRequest(
      registry,
      profile,
      catalog,
      registryProvider.GetCurrentRegistryHash()));

    Assert.True(report.IsCompatible);
    Assert.Equal(registryProvider.GetCurrentRegistryHash(), report.RegistryHash);
    Assert.Empty(report.Diagnostics);
  }

  [Fact]
  public void AuditProducesDeterministicCapabilityDiagnostics()
  {
    var registryProvider = new DefaultEngineCapabilityRegistryProvider();
    var registry = registryProvider.GetCurrentRegistry();
    var unsupportedCard = CreateSupportedCard() with
    {
      EngineVersion = "engine-future",
      Effects =
      [
        CreateSupportedEffect() with
        {
          Actions =
          [
            new AddCardToHandActionDefinition(
              "action_future",
              new CardSource("CARD_REF", "card:token"),
              PlayerSelector.Self,
              "FIZZLE_IF_FULL",
              null)
          ]
        }
      ]
    };
    var catalog = CreateCatalog(unsupportedCard);
    var profile = TestRulesetProfileFactory.CreateProfile("resolved-ruleset-mvp-core-v1");
    var service = new EngineCapabilityAuditService();

    var first = service.Audit(new EngineCapabilityAuditRequest(
      registry,
      profile,
      catalog,
      "sha256:not-current"));
    var second = service.Audit(new EngineCapabilityAuditRequest(
      registry,
      profile,
      catalog,
      "sha256:not-current"));

    Assert.False(first.IsCompatible);
    Assert.Equal(first.Diagnostics, second.Diagnostics);
    Assert.Contains(first.Diagnostics, diagnostic =>
      diagnostic.Code == "ENGINE_CAPABILITY_REGISTRY_HASH_MISMATCH"
      && diagnostic.Path == "engineCapabilityRegistry.hash");
    Assert.Contains(first.Diagnostics, diagnostic =>
      diagnostic.Code == "UNSUPPORTED_ACTION"
      && diagnostic.Path.Contains("actions[0]", StringComparison.Ordinal));
    Assert.Contains(first.Diagnostics, diagnostic =>
      diagnostic.Code == "UNSUPPORTED_ENGINE_VERSION");
  }

  internal static CatalogSnapshot CreateCatalog(CardDefinition card) =>
    new(
      "catalog-alpha",
      "ruleset_mvp_core_v1",
      "resolved-hash",
      DateTimeOffset.Parse("2026-06-26T00:00:00Z"),
      [
        new CardVersion(
          card.CardId!,
          card.CardVersion!,
          card.SchemaVersion,
          card.EngineVersion,
          "catalog-alpha",
          "pipeline-v1",
          "ruleset_mvp_core_v1",
          "resolved-hash",
          DateTimeOffset.Parse("2026-06-26T00:00:00Z"),
          card)
      ]);

  internal static CardDefinition CreateSupportedCard() =>
    new(
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
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
        "capability-audit-test",
        DateTimeOffset.Parse("2026-06-26T00:00:00Z"),
        "pipeline-v1",
        ["tests/Game.Application.Tests/EngineCapabilityAuditServiceTests.cs"]));

  internal static EffectDefinition CreateSupportedEffect() =>
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
        "FIZZLE"),
      [
        new DealDamageActionDefinition(
          "action_damage",
          new ValueExpression("FIXED", 3, null),
          new TargetRef("targetRequirement"),
          null,
          Lifesteal: false,
          Preventable: true)
      ],
      new ResolutionPolicy("SOURCE_ORDER"),
      new FizzlePolicy("FIZZLE"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      ["BATTLECRY"]);
}

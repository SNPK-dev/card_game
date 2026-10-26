using System.Reflection;
using System.Text.Json;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Shared.Tests;

public sealed class CardDslContractTests
{
  [Fact]
  public void CardDefinitionSerializesDocumentedEnumValues()
  {
    var card = CreateBattlecryMinion();

    var json = JsonSerializer.Serialize(card);

    Assert.Contains("\"CardType\":\"MINION_CARD\"", json);
    Assert.Contains("\"Rarity\":\"RARE\"", json);
    Assert.Contains("\"EffectKind\":\"SIMPLE_PLAY_EFFECT\"", json);
    Assert.Contains("\"SourceZones\":[\"HAND\"]", json);
    Assert.Contains("\"Phase\":\"ON_PLAY\"", json);
  }

  [Fact]
  public void ActionDefinitionSerializesAsSpecializedTaggedUnionVariant()
  {
    ActionDefinition action = new DealDamageActionDefinition(
      "action_001",
      new ValueExpression("FIXED", 2, null),
      new TargetRef("targetRequirement"),
      "ARCANE",
      Lifesteal: false,
      Preventable: true);

    var json = JsonSerializer.Serialize(action);

    Assert.Contains("\"actionType\":\"DEAL_DAMAGE\"", json);
    Assert.Contains("\"Amount\"", json);
    Assert.Contains("\"Target\"", json);
    Assert.DoesNotContain("\"Payload\"", json);
  }

  [Fact]
  public void CardDefinitionRoundTripsPolymorphicActionVariant()
  {
    var card = CreateBattlecryMinion();
    var json = JsonSerializer.Serialize(card);

    var roundTripped = JsonSerializer.Deserialize<CardDefinition>(json);

    Assert.NotNull(roundTripped);
    var action = Assert.IsType<DealDamageActionDefinition>(roundTripped.Effects.Single().Actions.Single());
    Assert.Equal("action_damage_001", action.ActionId);
    Assert.Equal(3, action.Amount.FixedValue);
  }

  [Fact]
  public void CardDslContractsDoNotExposeRuntimeBehaviorServices()
  {
    var contractTypes = typeof(CardDefinition).Assembly
      .GetExportedTypes()
      .Where(type =>
        type.Namespace is "Game.Shared.Dsl" or "Game.Shared.Primitives")
      .ToArray();

    Assert.NotEmpty(contractTypes);
    Assert.All(contractTypes, type =>
    {
      Assert.False(type.Name.EndsWith("Service", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Resolver", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Validator", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Executor", StringComparison.Ordinal));

      var declaredMethods = type
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(method => !method.IsSpecialName)
        .Select(method => method.Name)
        .ToArray();

      Assert.DoesNotContain("Execute", declaredMethods);
      Assert.DoesNotContain("Resolve", declaredMethods);
      Assert.DoesNotContain("Validate", declaredMethods);
      Assert.DoesNotContain("Mutate", declaredMethods);
    });
  }

  private static CardDefinition CreateBattlecryMinion()
  {
    return new CardDefinition(
      "card_arcane_apprentice",
      "1.0.0",
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog_alpha",
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
        new MinionDefinition(
          2,
          2,
          ["APPRENTICE"],
          [],
          CanAttack: true),
        null,
        null,
        null,
        null,
        null),
      [
        new EffectDefinition(
          "effect_battlecry_damage",
          EffectKind.SimplePlayEffect,
          [SourceZone.Hand],
          null,
          new TimingPolicy(
            Phase.OnPlay,
            null,
            0,
            "SOURCE_ORDER"),
          [],
          new TargetSpec(
            Required: true,
            TargetSelectionMode.PlayerChosen,
            new EntityQuery(
              "ANY",
              [SourceZone.Board],
              [EntityType.Minion],
              [],
              [],
              AliveOnly: true),
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
          new ResolutionPolicy("SEQUENTIAL"),
          new FizzlePolicy("FIZZLE_WHOLE_EFFECT"),
          new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
          ["BATTLECRY"])
      ],
      new CardTextGeneration(
        "Battlecry: Deal 3 damage.",
        "A small spark, neatly cataloged."),
      new BalanceMetadata(
        8,
        0,
        []),
      new AssetMetadata(
        null,
        null,
        null),
      new AuthoringMetadata(
        "contract-test",
        DateTimeOffset.Parse("2026-06-23T00:00:00Z"),
        "manual-fixture",
        ["docs/generator/dsl_reference.md"]));
  }
}

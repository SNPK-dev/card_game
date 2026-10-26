using System.Reflection;
using System.Text.Json;
using Game.Shared.Engine;
using Xunit;

namespace Game.Shared.Tests;

public sealed class EngineCapabilityContractTests
{
  [Fact]
  public void EngineCapabilityRegistrySerializesSupportedCapabilityLists()
  {
    var registry = CreateRegistry();

    var json = JsonSerializer.Serialize(registry);

    Assert.Contains("\"EngineVersion\":\"engine-v1\"", json);
    Assert.Contains("\"SupportedSchemaVersions\":[\"dsl-v1\"]", json);
    Assert.Contains("\"SupportedPrimitiveRegistryVersions\":[\"primitive-v1\"]", json);
    Assert.Contains("\"SupportedRulesetProfileVersions\":[\"0.2.0\",\"ruleset_mvp_core_v1\"]", json);
    Assert.Contains("\"SupportedActionTypes\":[\"DEAL_DAMAGE\",\"DRAW_CARD\",\"SUMMON\"]", json);
    Assert.Contains("\"SupportedEffectKinds\":[\"SIMPLE_PLAY_EFFECT\"]", json);
    Assert.Contains("\"SupportedMechanics\":[\"BATTLECRY\"]", json);
    Assert.Contains("\"SupportedPhases\":[\"ON_PLAY\"]", json);
    Assert.Contains("\"SupportedSourceZones\":[\"HAND\"]", json);
  }

  [Fact]
  public void EngineCapabilityRegistryCarriesMetadataWithoutCalculatingHashes()
  {
    var registry = CreateRegistry();

    Assert.Equal("engine-v1", registry.EngineVersion);
    Assert.Contains("ruleset_mvp_core_v1", registry.SupportedRulesetProfileVersions);
    Assert.Contains("docs/engine/main.md", registry.Metadata.SourceReferences);
    Assert.DoesNotContain(
      typeof(EngineCapabilityRegistry).GetProperties().Select(property => property.Name),
      name => name.Contains("Hash", StringComparison.Ordinal));
  }

  [Fact]
  public void EngineCapabilityContractsDoNotExposeRuntimeBehaviorServices()
  {
    var contractTypes = typeof(EngineCapabilityRegistry).Assembly
      .GetExportedTypes()
      .Where(type => type.Namespace is "Game.Shared.Engine")
      .ToArray();

    Assert.NotEmpty(contractTypes);
    Assert.All(contractTypes, type =>
    {
      Assert.True(type.IsEnum || IsRecord(type));
      Assert.False(type.Name.EndsWith("Service", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Resolver", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Validator", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Executor", StringComparison.Ordinal));

      var declaredMethods = type
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(method => !method.IsSpecialName)
        .Select(method => method.Name)
        .ToArray();

      Assert.DoesNotContain("Validate", declaredMethods);
      Assert.DoesNotContain("CalculateHash", declaredMethods);
      Assert.DoesNotContain("BuildGenerationContext", declaredMethods);
      Assert.DoesNotContain("Execute", declaredMethods);
    });
  }

  private static EngineCapabilityRegistry CreateRegistry()
  {
    return new EngineCapabilityRegistry(
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
        "bootstrap",
        DateTimeOffset.Parse("2026-06-23T00:00:00Z"),
        "bootstrap",
        DateTimeOffset.Parse("2026-06-23T00:00:00Z"),
        [
          "docs/engine/main.md",
          "docs/engine/canonical_serialization_spec.md"
        ],
        ["Contract test fixture."]));
  }

  private static bool IsRecord(Type type)
  {
    return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
      .Any(method => method.Name == "<Clone>$");
  }
}

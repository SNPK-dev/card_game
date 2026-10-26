using Game.Application.Capabilities;
using Xunit;

namespace Game.Application.Tests;

public sealed class EngineCapabilityRegistryProviderTests
{
  [Fact]
  public void CurrentRegistryAggregatesRuntimeCapabilities()
  {
    var provider = new DefaultEngineCapabilityRegistryProvider();

    var registry = provider.GetCurrentRegistry();

    Assert.Equal("engine-bootstrap-0.3.0", registry.EngineVersion);
    Assert.Contains("ruleset_mvp_core_v1", registry.SupportedRulesetProfileVersions);
    Assert.Contains("PLAY_CARD", registry.SupportedActionTypes);
    Assert.Contains("RESOLVE_CHOICE", registry.SupportedActionTypes);
    Assert.Contains("ATTACK", registry.SupportedActionTypes);
    Assert.Contains("CONCEDE", registry.SupportedActionTypes);
    Assert.Contains("DEAL_DAMAGE", registry.SupportedActionTypes);
    Assert.Contains("APPLY_MODIFIER", registry.SupportedActionTypes);
    Assert.Contains("APPLY_AURA", registry.SupportedActionTypes);
    Assert.Contains("DISCOVER", registry.SupportedActionTypes);
    Assert.Contains("RANDOM_SELECT", registry.SupportedActionTypes);
    Assert.Contains("REPLACE_EVENT", registry.SupportedActionTypes);
    Assert.Contains("RESURRECT", registry.SupportedActionTypes);
    Assert.Contains("RETURN_TO_HAND", registry.SupportedActionTypes);
    Assert.Contains("MUTATION_BATCH_PIPELINE", registry.SupportedMechanics);
    Assert.Contains("COMMITTED_EVENT_MAPPING", registry.SupportedMechanics);
    Assert.Contains("DETERMINISTIC_RNG_ROLL_LOGGING", registry.SupportedMechanics);
    Assert.Contains("DISCOVER_FIXED_POOL_CHOICE", registry.SupportedMechanics);
    Assert.Contains("DISCOVER_SELECTED_CARD_MATERIALIZATION", registry.SupportedMechanics);
    Assert.Contains("GRAVEYARD_RESURRECT", registry.SupportedMechanics);
    Assert.Contains("HAND_ZONE_MODIFIER", registry.SupportedMechanics);
    Assert.Contains("PRESENTATION_PLAYBACK_RUNTIME", registry.SupportedMechanics);
    Assert.Contains("RANDOM_TARGET_RESOLUTION", registry.SupportedMechanics);
    Assert.Contains("DIVINE_SHIELD", registry.SupportedMechanics);
    Assert.Contains("STEALTH", registry.SupportedMechanics);
    Assert.Contains("TAUNT", registry.SupportedMechanics);
    Assert.Contains("ON_PLAY", registry.SupportedPhases);
    Assert.Contains("HAND", registry.SupportedSourceZones);
    Assert.Contains("docs/engine/main.md", registry.Metadata.SourceReferences);
    Assert.Contains("src/Game.Domain/Actions", registry.Metadata.SourceReferences);
    Assert.Contains("src/Game.Domain/Randomness/DeterministicRngRoller.cs", registry.Metadata.SourceReferences);
  }

  [Fact]
  public void CurrentRegistryUsesDeterministicOrdering()
  {
    var provider = new DefaultEngineCapabilityRegistryProvider();

    var registry = provider.GetCurrentRegistry();

    AssertSorted(registry.SupportedSchemaVersions);
    AssertSorted(registry.SupportedPrimitiveRegistryVersions);
    AssertSorted(registry.SupportedRulesetProfileVersions);
    AssertSorted(registry.SupportedActionTypes);
    AssertSorted(registry.SupportedEffectKinds);
    AssertSorted(registry.SupportedMechanics);
    AssertSorted(registry.SupportedPhases);
    AssertSorted(registry.SupportedSourceZones);
    AssertSorted(registry.SupportedResolutionPolicies);
    AssertSorted(registry.SupportedFizzlePolicies);
    AssertSorted(registry.SupportedAssetManifestVersions);
    AssertSorted(registry.Metadata.SourceReferences);
    AssertSorted(registry.Metadata.Notes);
  }

  [Fact]
  public void CurrentRegistryDoesNotUseRuntimeGeneratedMetadata()
  {
    var provider = new DefaultEngineCapabilityRegistryProvider();

    var first = provider.GetCurrentRegistry();
    var second = provider.GetCurrentRegistry();

    Assert.Same(first, second);
    Assert.Equal(DateTimeOffset.Parse("2026-06-26T00:00:00Z"), first.Metadata.AuthoredAt);
    Assert.Equal(first.Metadata.AuthoredAt, first.Metadata.ReviewedAt);
  }

  [Fact]
  public void CurrentRegistryHashIsStableAndChangesWhenRuntimeCapabilitiesDrift()
  {
    var provider = new DefaultEngineCapabilityRegistryProvider();
    var builder = new RuntimeEngineCapabilityRegistryBuilder();
    var metadata = provider.GetCurrentRegistry().Metadata;

    var baseline = provider.GetCurrentRegistryHash();
    var drifted = builder.Build(
      "engine-bootstrap-0.3.0",
      [
        ..DefaultRuntimeCapabilityDeclarations.Create(),
        new RuntimeCapabilityDeclaration(
          "future-action-probe",
          ActionTypes: ["FUTURE_ACTION"])
      ],
      metadata).RegistryHash;

    Assert.StartsWith("sha256:", baseline, StringComparison.Ordinal);
    Assert.NotEqual(baseline, drifted);
  }

  [Fact]
  public void RegistryBuilderRejectsDuplicateMutationCapabilities()
  {
    var builder = new RuntimeEngineCapabilityRegistryBuilder();
    var metadata = new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry().Metadata;

    var exception = Assert.Throws<InvalidOperationException>(() =>
      builder.Build(
        "engine-test",
        [
          new RuntimeCapabilityDeclaration("mutation-a", MutationTypes: ["APPLY_DAMAGE"], EventMappedMutationTypes: ["APPLY_DAMAGE"]),
          new RuntimeCapabilityDeclaration("mutation-b", MutationTypes: ["APPLY_DAMAGE"], EventMappedMutationTypes: ["APPLY_DAMAGE"])
        ],
        metadata));

    Assert.Contains("Duplicate mutation type 'APPLY_DAMAGE'", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void RegistryBuilderRejectsMutationWithoutCommittedEventMapping()
  {
    var builder = new RuntimeEngineCapabilityRegistryBuilder();
    var metadata = new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry().Metadata;

    var exception = Assert.Throws<InvalidOperationException>(() =>
      builder.Build(
        "engine-test",
        [new RuntimeCapabilityDeclaration("mutation-a", MutationTypes: ["APPLY_DAMAGE"])],
        metadata));

    Assert.Contains("Mutation type 'APPLY_DAMAGE' has no committed event mapping capability.", exception.Message, StringComparison.Ordinal);
  }

  private static void AssertSorted(IReadOnlyList<string> values)
  {
    Assert.Equal(values.Order(StringComparer.Ordinal), values);
  }
}

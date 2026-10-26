using System.Reflection;
using System.Text.Json;
using Game.Shared.Schema;
using Xunit;

namespace Game.Shared.Tests;

public sealed class SharedContractSchemaTests
{
  [Fact]
  public void SchemaManifestSerializesAsReviewableData()
  {
    var manifest = CreateManifest();

    var json = JsonSerializer.Serialize(manifest);

    Assert.Contains("\"Category\":\"DSL\"", json, StringComparison.Ordinal);
    Assert.Contains("\"ContractName\":\"CardDefinition\"", json, StringComparison.Ordinal);
    Assert.Contains("\"FieldNames\":[\"CardId\",\"CardVersion\",\"SchemaVersion\"]", json, StringComparison.Ordinal);
    Assert.Contains("\"EnumValues\":[\"ON_PLAY\",\"TURN_END\"]", json, StringComparison.Ordinal);
  }

  [Fact]
  public void SchemaManifestKeepsCallerAuthoredEntries()
  {
    var manifest = CreateManifest();

    Assert.Equal("shared-contract-schema-manifest-v1", manifest.ManifestVersion);
    Assert.Equal(
      [
        SharedContractSchemaCategory.Dsl,
        SharedContractSchemaCategory.Event,
        SharedContractSchemaCategory.Mutation,
        SharedContractSchemaCategory.Catalog,
        SharedContractSchemaCategory.Governance,
        SharedContractSchemaCategory.EngineCapability,
        SharedContractSchemaCategory.Deck,
        SharedContractSchemaCategory.Asset,
        SharedContractSchemaCategory.Primitive,
        SharedContractSchemaCategory.Schema
      ],
      manifest.Entries.Select(entry => entry.Category));
  }

  [Fact]
  public void SchemaNamespaceRemainsDataOnly()
  {
    var schemaTypes = typeof(SharedContractSchemaManifest)
      .Assembly
      .GetExportedTypes()
      .Where(type => type.Namespace == "Game.Shared.Schema")
      .ToArray();

    Assert.NotEmpty(schemaTypes);
    Assert.All(schemaTypes, type =>
    {
      Assert.True(type.IsEnum || IsRecord(type), $"{type.FullName} must remain an enum or record.");
      Assert.DoesNotContain(
        type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
        method => !method.IsSpecialName
          && method.Name is not "ToString" and not "GetHashCode" and not "Equals" and not "Deconstruct" and not "<Clone>$");
    });
  }

  private static SharedContractSchemaManifest CreateManifest() =>
    new(
      "shared-contract-schema-manifest-v1",
      "0.1.0",
      DateTimeOffset.Parse("2026-06-25T00:00:00Z"),
      "contract-test",
      [
        new SharedContractSchemaEntry(
          "CardDefinition",
          "Game.Shared.Dsl",
          SharedContractSchemaCategory.Dsl,
          "record",
          ["CardId", "CardVersion", "SchemaVersion"],
          []),
        new SharedContractSchemaEntry(
          "CommittedEventRecord",
          "Game.Shared.Events",
          SharedContractSchemaCategory.Event,
          "record",
          ["EventSeq", "CommandSeq", "EventType"],
          []),
        new SharedContractSchemaEntry(
          "ProposedMutationBatchRecord",
          "Game.Shared.Mutation",
          SharedContractSchemaCategory.Mutation,
          "record",
          ["BatchId", "CommandSeq", "PreStateHash"],
          []),
        new SharedContractSchemaEntry(
          "CatalogSnapshot",
          "Game.Shared.Catalog",
          SharedContractSchemaCategory.Catalog,
          "record",
          ["SnapshotId", "RulesetProfileId", "RulesetProfileHash"],
          []),
        new SharedContractSchemaEntry(
          "ResolvedRulesetProfile",
          "Game.Shared.Ruleset",
          SharedContractSchemaCategory.Governance,
          "record",
          ["Id", "SourceProfileId", "ResolvedHash"],
          []),
        new SharedContractSchemaEntry(
          "EngineCapabilityRegistry",
          "Game.Shared.Engine",
          SharedContractSchemaCategory.EngineCapability,
          "record",
          ["EngineVersion", "SupportedActionTypes", "SupportedEffectKinds"],
          []),
        new SharedContractSchemaEntry(
          "DeckDefinition",
          "Game.Shared.Decks",
          SharedContractSchemaCategory.Deck,
          "record",
          ["DeckId", "PlayerId", "Cards"],
          []),
        new SharedContractSchemaEntry(
          "AssetManifest",
          "Game.Shared.Assets",
          SharedContractSchemaCategory.Asset,
          "record",
          ["ManifestId", "ManifestVersion", "Visual"],
          []),
        new SharedContractSchemaEntry(
          "Phase",
          "Game.Shared.Primitives",
          SharedContractSchemaCategory.Primitive,
          "enum",
          [],
          ["ON_PLAY", "TURN_END"]),
        new SharedContractSchemaEntry(
          "SharedContractSchemaManifest",
          "Game.Shared.Schema",
          SharedContractSchemaCategory.Schema,
          "record",
          ["ManifestVersion", "SharedAssemblyVersion", "Entries"],
          [])
      ]);

  private static bool IsRecord(Type type) =>
    type.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null
    || type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
      .Any(method => method.Name == "Deconstruct");
}

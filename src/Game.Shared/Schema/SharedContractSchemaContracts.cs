using System.Text.Json.Serialization;

namespace Game.Shared.Schema;

[JsonConverter(typeof(JsonStringEnumConverter<SharedContractSchemaCategory>))]
public enum SharedContractSchemaCategory
{
  [JsonStringEnumMemberName("SCHEMA")]
  Schema,

  [JsonStringEnumMemberName("GOVERNANCE")]
  Governance,

  [JsonStringEnumMemberName("DSL")]
  Dsl,

  [JsonStringEnumMemberName("ENGINE_CAPABILITY")]
  EngineCapability,

  [JsonStringEnumMemberName("EVENT")]
  Event,

  [JsonStringEnumMemberName("MUTATION")]
  Mutation,

  [JsonStringEnumMemberName("CATALOG")]
  Catalog,

  [JsonStringEnumMemberName("DECK")]
  Deck,

  [JsonStringEnumMemberName("ASSET")]
  Asset,

  [JsonStringEnumMemberName("PRIMITIVE")]
  Primitive
}

public sealed record SharedContractSchemaManifest(
  string ManifestVersion,
  string SharedAssemblyVersion,
  DateTimeOffset AuthoredAt,
  string AuthoredBy,
  IReadOnlyList<SharedContractSchemaEntry> Entries);

public sealed record SharedContractSchemaEntry(
  string ContractName,
  string Namespace,
  SharedContractSchemaCategory Category,
  string ContractKind,
  IReadOnlyList<string> FieldNames,
  IReadOnlyList<string> EnumValues);

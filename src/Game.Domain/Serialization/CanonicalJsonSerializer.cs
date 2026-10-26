using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Game.Domain.Serialization;

public sealed class CanonicalJsonSerializer : ICanonicalSerializer
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
  {
    WriteIndented = false
  };

  public byte[] SerializeToCanonicalBytes<T>(T value)
  {
    var node = JsonSerializer.SerializeToNode(value, JsonOptions);
    var canonicalNode = SortNode(node);
    var json = canonicalNode?.ToJsonString(JsonOptions) ?? "null";
    return Encoding.UTF8.GetBytes(json);
  }

  private static JsonNode? SortNode(JsonNode? node)
  {
    return node switch
    {
      JsonObject jsonObject => SortObject(jsonObject),
      JsonArray jsonArray => SortArray(jsonArray),
      _ => node?.DeepClone()
    };
  }

  private static JsonObject SortObject(JsonObject jsonObject)
  {
    var sorted = new JsonObject();
    foreach (var property in jsonObject.OrderBy(item => item.Key, StringComparer.Ordinal))
    {
      sorted[property.Key] = SortNode(property.Value);
    }

    return sorted;
  }

  private static JsonArray SortArray(JsonArray jsonArray)
  {
    var sorted = new JsonArray();
    foreach (var item in jsonArray)
    {
      sorted.Add(SortNode(item));
    }

    return sorted;
  }
}


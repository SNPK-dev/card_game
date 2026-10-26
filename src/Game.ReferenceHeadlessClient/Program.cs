using System.Text.Json;
using System.Text.Json.Serialization;
using Game.ReferenceHeadlessClient;

var baseUrl = args.Length > 0 ? args[0] : "http://localhost:5000";
var request = new ReferenceHeadlessClientRequest(
  MatchId: args.Length > 1 ? args[1] : "reference-headless-match",
  Deck1Id: args.Length > 2 ? args[2] : "deck-p1",
  Deck2Id: args.Length > 3 ? args[3] : "deck-p2");

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
  Converters = { new JsonStringEnumConverter() }
};

using var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
var report = await ReferenceHeadlessClientRunner.RunAsync(http, request);

Console.WriteLine(JsonSerializer.Serialize(report, json));

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Game.Generator.Providers;
using Game.Shared.Dsl;
using Game.Shared.Primitives;

namespace Game.Infrastructure.Generation;

public sealed class OpenAiStructuredCardGenerationProvider(HttpClient httpClient, Uri endpoint, string model, string apiKey) : IStructuredCardGenerationProvider
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  public StructuredCardGenerationResult Generate(StructuredCardGenerationRequest request)
  {
    if (string.IsNullOrWhiteSpace(apiKey)) return Failure(request, "OPENAI_API_KEY_MISSING", "OPENAI_API_KEY is not configured.");
    try
    {
      using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
      message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
      message.Content = JsonContent.Create(new
      {
        model,
        input = "Create the original name, balanced numeric direction, and premium visual concept for one collectible card. " + request.Prompt + "\nReturn only the requested JSON object. Use HERO_CARD when the player explicitly asks for a hero or hero card; otherwise use MINION_CARD. HERO_CARD uses armorGain and may replace the hero power. Use zero attack/health for HERO_CARD and zero armorGain/heroPowerCost for MINION_CARD. Use a short unique name. The authoritative server validates every field.",
        reasoning = new { effort = "low" },
        max_output_tokens = 800,
        text = new
        {
          format = new
          {
            type = "json_schema",
            name = "chronicle_card",
            strict = true,
            schema = new
            {
              type = "object",
              additionalProperties = false,
              properties = new
              {
                name = new { type = "string", minLength = 2, maxLength = 48 },
                cardType = new { type = "string", @enum = new[] { "MINION_CARD", "HERO_CARD" } },
                manaCost = new { type = "integer", minimum = 1, maximum = 10 },
                attack = new { type = "integer", minimum = 0, maximum = 12 },
                health = new { type = "integer", minimum = 0, maximum = 12 },
                armorGain = new { type = "integer", minimum = 0, maximum = 15 },
                heroPowerCost = new { type = "integer", minimum = 0, maximum = 10 },
                heroPowerActivationTiming = new { type = "string", @enum = new[] { "ACTIVE_PLAYER_MAIN" } },
                visualPrompt = new { type = "string", minLength = 12, maxLength = 500 }
              },
              required = new[] { "name", "cardType", "manaCost", "attack", "health", "armorGain", "heroPowerCost", "heroPowerActivationTiming", "visualPrompt" }
            }
          }
        }
      }, options: JsonOptions);
      using var response = httpClient.Send(message);
      var raw = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
      if (!response.IsSuccessStatusCode) return Failure(request, $"OPENAI_HTTP_{(int)response.StatusCode}", SafeError(raw));
      using var document = JsonDocument.Parse(raw);
      var text = ExtractOutputText(document.RootElement);
      if (string.IsNullOrWhiteSpace(text)) return Failure(request, "OPENAI_OUTPUT_MISSING", "OpenAI response did not contain output text.");
      var card = JsonSerializer.Deserialize<CardJson>(text, JsonOptions);
      if (card is null) return Failure(request, "OPENAI_SCHEMA_INVALID", "OpenAI card JSON was empty.");
      var heroCard=card.CardType is CardType.HeroCard;
      var output = new StructuredCardOutput(card.Name.Trim(), card.CardType, ["mage"], heroCard?Rarity.Legendary:Rarity.Common, card.ManaCost, [], [], true, ["standard"], heroCard?null:card.Attack, heroCard?null:Math.Max(1,card.Health), !heroCard, false, heroCard?$"Gain {card.ArmorGain} Armor. Replace your Hero Power.":string.Empty, [], 1, 0, [],
        new AssetGenerationPrompt(card.VisualPrompt.Trim() + ", premium collectible card illustration, no text, no logo", "chronicle-forge-v1", "safe-card-art-v1"),
        new AssetGenerationPrompt("subtle magical idle motion", "chronicle-forge-v1", "safe-card-art-v1"), null, [], "NONE",
        HeroArmorGain:heroCard?card.ArmorGain:0,
        HeroPowerCost:heroCard&&card.HeroPowerCost>0?card.HeroPowerCost:null,
        HeroPowerActivationTiming:card.HeroPowerActivationTiming);
      return StructuredCardGenerationResult.Success(output, "openai", model, $"openai-response:{request.GenerationId}");
    }
    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
    {
      return Failure(request, exception is TaskCanceledException ? "OPENAI_TIMEOUT" : "OPENAI_REQUEST_FAILED", exception.Message);
    }
  }

  private StructuredCardGenerationResult Failure(StructuredCardGenerationRequest request, string code, string message) =>
    StructuredCardGenerationResult.Failure("openai", model, $"openai-response:{request.GenerationId}:failed", code, message);

  private static string ExtractOutputText(JsonElement root)
  {
    if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return string.Empty;
    foreach (var item in output.EnumerateArray())
      if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        foreach (var part in content.EnumerateArray())
          if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text" && part.TryGetProperty("text", out var text)) return text.GetString() ?? string.Empty;
    return string.Empty;
  }

  private static string SafeError(string raw)
  {
    try
    {
      using var document = JsonDocument.Parse(raw);
      if (document.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message)) return message.GetString() ?? "OpenAI request failed.";
    }
    catch (JsonException) { }
    return "OpenAI request failed.";
  }

  private sealed record CardJson(string Name,CardType CardType,int ManaCost,int Attack,int Health,int ArmorGain,int HeroPowerCost,string HeroPowerActivationTiming,string VisualPrompt);
}

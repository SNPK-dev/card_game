using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Game.Generator.Providers;

namespace Game.Infrastructure.Generation;

public sealed class OpenAiGeneratedAssetProvider(HttpClient httpClient, Uri endpoint, string model, string apiKey, string assetRoot) : IGeneratedAssetProvider
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  public GeneratedAssetProviderResult Generate(GeneratedAssetProviderRequest request)
  {
    if (string.IsNullOrWhiteSpace(apiKey)) return Failure(request, "OPENAI_API_KEY_MISSING", "OPENAI_API_KEY is not configured.");
    try
    {
      using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
      message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
      message.Content = JsonContent.Create(new { model, prompt = request.Prompt, size = "1024x1024", quality = "low", output_format = "png" }, options: JsonOptions);
      using var response = httpClient.Send(message);
      var raw = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
      if (!response.IsSuccessStatusCode) return Failure(request, $"OPENAI_IMAGE_HTTP_{(int)response.StatusCode}", SafeError(raw));
      using var document = JsonDocument.Parse(raw);
      if (!document.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0 || !data[0].TryGetProperty("b64_json", out var encoded))
        return Failure(request, "OPENAI_IMAGE_OUTPUT_MISSING", "OpenAI image response did not contain b64_json.");
      var bytes = Convert.FromBase64String(encoded.GetString() ?? string.Empty);
      if (bytes.Length < 256) return Failure(request, "OPENAI_IMAGE_EMPTY", "OpenAI image output was empty.");
      var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
      var directory = Path.Combine(assetRoot, "generated-assets");
      Directory.CreateDirectory(directory);
      var path = Path.GetFullPath(Path.Combine(directory, request.CardVersionId + ".png"));
      File.WriteAllBytes(path, bytes);
      return GeneratedAssetProviderResult.Success("openai", model, path, "png", hash, "APPROVED", $"openai-image:{request.GenerationId}");
    }
    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or FormatException or IOException)
    {
      return Failure(request, exception is TaskCanceledException ? "OPENAI_IMAGE_TIMEOUT" : "OPENAI_IMAGE_REQUEST_FAILED", exception.Message);
    }
  }

  private GeneratedAssetProviderResult Failure(GeneratedAssetProviderRequest request, string code, string message) =>
    GeneratedAssetProviderResult.Failure("openai", model, $"openai-image:{request.GenerationId}:failed", code, message);

  private static string SafeError(string raw)
  {
    try { using var document = JsonDocument.Parse(raw); if (document.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message)) return message.GetString() ?? "OpenAI image request failed."; }
    catch (JsonException) { }
    return "OpenAI image request failed.";
  }
}

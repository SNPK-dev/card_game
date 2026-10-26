using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Game.Generator.Providers;

namespace Game.Infrastructure.Generation;

public sealed class HttpGeneratedAssetProvider(
  HttpClient httpClient,
  GenerationProviderConfiguration configuration) : IGeneratedAssetProvider
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  public GeneratedAssetProviderResult Generate(GeneratedAssetProviderRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    EnsureConfigured();

    Exception? lastException = null;
    string? lastErrorCode = null;
    for (var attempt = 0; attempt <= configuration.MaxRetryAttempts; attempt++)
    {
      try
      {
        using var requestMessage = CreateMessage(new
        {
          request.GenerationId,
          request.CardId,
          request.CardVersionId,
          request.AssetKind,
          request.Prompt,
          request.StyleGuideRef,
          request.SafetyPolicyRef,
          ModelId = configuration.ModelId,
          TimeoutMs = (int)request.Timeout.TotalMilliseconds
        });
        using var response = httpClient.Send(requestMessage);
        if (!response.IsSuccessStatusCode)
        {
          lastErrorCode = ClassifyStatusCode((int)response.StatusCode);
          lastException = new HttpRequestException($"Asset provider returned {(int)response.StatusCode}.");
          Backoff(attempt);
          continue;
        }

        var body = response.Content.ReadFromJsonAsync<GeneratedAssetHttpResponse>(JsonOptions).GetAwaiter().GetResult();
        if (body is null)
        {
          return GeneratedAssetProviderResult.Failure(configuration.ProviderName, configuration.ModelId, $"asset:{request.GenerationId}:empty", "EMPTY_RESPONSE", "Asset provider returned an empty response.");
        }

        return GeneratedAssetProviderResult.Success(
          configuration.ProviderName,
          configuration.ModelId,
          body.AssetRef,
          body.Format,
          body.ContentHash,
          body.ModerationStatus,
          body.RawAssetRef);
      }
      catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
      {
        lastErrorCode = exception switch
        {
          TaskCanceledException => "TIMEOUT_PROVIDER_REQUEST",
          JsonException => "SCHEMA_PROVIDER_RESPONSE",
          _ => "TRANSIENT_PROVIDER_REQUEST"
        };
        lastException = exception;
        Backoff(attempt);
      }
    }

    return GeneratedAssetProviderResult.Failure(
      configuration.ProviderName,
      configuration.ModelId,
      $"asset:{request.GenerationId}:failed",
      lastErrorCode ?? "PROVIDER_REQUEST_FAILED",
      lastException?.Message ?? "Asset provider request failed.");
  }

  private void Backoff(int attempt)
  {
    if (attempt >= configuration.MaxRetryAttempts)
    {
      return;
    }

    Thread.Sleep(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt)));
  }

  private static string ClassifyStatusCode(int statusCode) =>
    statusCode switch
    {
      408 or 429 => "TRANSIENT_PROVIDER_HTTP",
      >= 500 and < 600 => "TRANSIENT_PROVIDER_HTTP",
      >= 400 and < 500 => "PERMANENT_PROVIDER_HTTP",
      _ => "PROVIDER_REQUEST_FAILED"
    };

  private HttpRequestMessage CreateMessage<TPayload>(TPayload payload)
  {
    var message = new HttpRequestMessage(HttpMethod.Post, configuration.Endpoint)
    {
      Content = JsonContent.Create(payload, options: JsonOptions)
    };
    if (!string.IsNullOrWhiteSpace(configuration.ApiKey))
    {
      message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);
    }

    return message;
  }

  private void EnsureConfigured()
  {
    var validation = new GenerationProviderConfigurationValidator().Validate(configuration);
    if (!validation.IsConfigured)
    {
      throw new InvalidOperationException(
        $"Asset provider configuration is incomplete: {string.Join(", ", validation.MissingFields)}.");
    }
  }

  private sealed record GeneratedAssetHttpResponse(
    string AssetRef,
    string Format,
    string ContentHash,
    string ModerationStatus,
    string RawAssetRef);
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Game.Generator.Providers;

namespace Game.Infrastructure.Generation;

public sealed class HttpStructuredCardGenerationProvider(
  HttpClient httpClient,
  GenerationProviderConfiguration configuration) : IStructuredCardGenerationProvider
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  public StructuredCardGenerationResult Generate(StructuredCardGenerationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    EnsureConfigured();

    return SendWithRetries(
      () => new
      {
        request.GenerationId,
        request.PromptVersion,
        ModelId = configuration.ModelId,
        request.Prompt,
        request.ContextRefs,
        TimeoutMs = (int)request.Timeout.TotalMilliseconds
      },
      response => response.Output is null
        ? StructuredCardGenerationResult.Failure(configuration.ProviderName, configuration.ModelId, response.RawStructuredOutputRef, "OUTPUT_MISSING", "Structured provider response did not contain output.")
        : StructuredCardGenerationResult.Success(response.Output, configuration.ProviderName, configuration.ModelId, response.RawStructuredOutputRef));
  }

  private StructuredCardGenerationResult SendWithRetries<TPayload>(
    Func<TPayload> payloadFactory,
    Func<StructuredCardHttpResponse, StructuredCardGenerationResult> map)
  {
    Exception? lastException = null;
    string? lastErrorCode = null;
    for (var attempt = 0; attempt <= configuration.MaxRetryAttempts; attempt++)
    {
      try
      {
        using var requestMessage = CreateMessage(payloadFactory());
        using var response = httpClient.Send(requestMessage);
        if (!response.IsSuccessStatusCode)
        {
          lastErrorCode = ClassifyStatusCode((int)response.StatusCode);
          lastException = new HttpRequestException($"Structured provider returned {(int)response.StatusCode}.");
          Backoff(attempt);
          continue;
        }

        var body = response.Content.ReadFromJsonAsync<StructuredCardHttpResponse>(JsonOptions).GetAwaiter().GetResult();
        if (body is null)
        {
          return StructuredCardGenerationResult.Failure(configuration.ProviderName, configuration.ModelId, $"raw:{configuration.ProviderName}:empty", "EMPTY_RESPONSE", "Structured provider returned an empty response.");
        }

        return map(body);
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

    return StructuredCardGenerationResult.Failure(
      configuration.ProviderName,
      configuration.ModelId,
      $"raw:{configuration.ProviderName}:failed",
      lastErrorCode ?? "PROVIDER_REQUEST_FAILED",
      lastException?.Message ?? "Structured provider request failed.");
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
        $"Structured provider configuration is incomplete: {string.Join(", ", validation.MissingFields)}.");
    }
  }

  private sealed record StructuredCardHttpResponse(
    StructuredCardOutput? Output,
    string RawStructuredOutputRef);
}

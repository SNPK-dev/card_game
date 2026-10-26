namespace Game.Generator.Providers;

public sealed record GenerationProviderConfiguration(
  string ProviderName,
  string ModelId,
  Uri? Endpoint,
  string? ApiKey,
  TimeSpan Timeout,
  int MaxRetryAttempts)
{
  public GenerationProviderConfiguration Redacted() =>
    this with
    {
      ApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : "***REDACTED***"
    };
}

public sealed record GenerationProviderConfigurationValidationResult(
  bool IsConfigured,
  IReadOnlyList<string> MissingFields,
  GenerationProviderConfiguration RedactedConfiguration,
  bool IsLocalConfigured = false);

public sealed class GenerationProviderConfigurationValidator
{
  public GenerationProviderConfigurationValidationResult Validate(GenerationProviderConfiguration configuration)
  {
    ArgumentNullException.ThrowIfNull(configuration);

    var missing = new List<string>();
    if (string.IsNullOrWhiteSpace(configuration.ProviderName))
    {
      missing.Add("providerName");
    }

    if (string.IsNullOrWhiteSpace(configuration.ModelId))
    {
      missing.Add("modelId");
    }

    if (configuration.Endpoint is null)
    {
      missing.Add("endpoint");
    }

    var isLocalEndpoint = IsLocalEndpoint(configuration.Endpoint);
    if (!isLocalEndpoint && string.IsNullOrWhiteSpace(configuration.ApiKey))
    {
      missing.Add("apiKey");
    }

    if (configuration.Timeout <= TimeSpan.Zero)
    {
      missing.Add("timeout");
    }

    if (configuration.MaxRetryAttempts < 0)
    {
      missing.Add("maxRetryAttempts");
    }

    return new GenerationProviderConfigurationValidationResult(
      missing.Count == 0,
      missing,
      configuration.Redacted(),
      missing.Count == 0 && isLocalEndpoint);
  }

  public static bool IsLocalEndpoint(Uri? endpoint)
  {
    if (endpoint is null)
    {
      return false;
    }

    return endpoint.IsLoopback
      || endpoint.Scheme.Equals("local", StringComparison.OrdinalIgnoreCase)
      || endpoint.Host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase);
  }
}

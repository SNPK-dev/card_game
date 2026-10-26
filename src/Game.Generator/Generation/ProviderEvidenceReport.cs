using System.Security.Cryptography;
using System.Text;
using Game.Generator.Providers;

namespace Game.Generator.Generation;

public sealed record ProviderEvidenceReport(
  string ReportId,
  DateTimeOffset CreatedAt,
  ProductionCardGenerationStatus Status,
  IReadOnlyList<ProviderConfigurationEvidence> Providers,
  IReadOnlyList<ProviderArtifactEvidence> Artifacts,
  IReadOnlyList<string> Diagnostics,
  IReadOnlyList<string> RedactedProviderRefs,
  bool HasLiveProviderEvidence = false,
  bool HasConfiguredProviderEvidence = false);

public sealed record ProviderConfigurationEvidence(
  string ProviderRole,
  string ProviderName,
  string ModelId,
  string? EndpointHost,
  string ApiKeyStatus,
  int TimeoutSeconds,
  int MaxRetryAttempts,
  bool IsConfiguredForLiveEndpoint = false,
  string EvidenceClassification = "BLOCKED_OR_LOCAL");

public sealed record ProviderArtifactEvidence(
  string ArtifactKind,
  string ArtifactRef,
  string ContentHash);

public sealed record ProviderEvidenceReportRequest(
  string ReportId,
  DateTimeOffset CreatedAt,
  RealProviderGenerationHarnessResult HarnessResult);

public sealed class ProviderEvidenceReportBuilder
{
  public ProviderEvidenceReport Build(ProviderEvidenceReportRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.HarnessResult);
    if (string.IsNullOrWhiteSpace(request.ReportId))
    {
      throw new ArgumentException("Provider evidence report id is required.", nameof(request));
    }

    var providers = request.HarnessResult.RedactedProviderConfigurations
      .Select((configuration, index) => ToConfigurationEvidence(Role(index), configuration))
      .ToArray();
    var artifacts = CreateArtifacts(request.HarnessResult.GenerationResult);
    var diagnostics = request.HarnessResult.Diagnostics
      .Select(diagnostic => $"{diagnostic.Code}:{diagnostic.Path}:{diagnostic.Identifier}:{diagnostic.Message}")
      .Order(StringComparer.Ordinal)
      .ToArray();
    var providerRefs = request.HarnessResult.RedactedProviderConfigurations
      .Select(configuration =>
        $"provider:{configuration.ProviderName}:model:{configuration.ModelId}:endpoint:{configuration.Endpoint?.Host ?? "missing"}:key:{(string.IsNullOrWhiteSpace(configuration.ApiKey) ? "missing" : "redacted")}")
      .Order(StringComparer.Ordinal)
      .ToArray();

    return new ProviderEvidenceReport(
      request.ReportId,
      request.CreatedAt,
      request.HarnessResult.Status,
      providers,
      artifacts,
      diagnostics,
      providerRefs,
      request.HarnessResult.Status is ProductionCardGenerationStatus.Succeeded
        && providers.Length > 0
        && providers.All(provider => provider.IsConfiguredForLiveEndpoint)
        && artifacts.Count > 0,
      request.HarnessResult.Status is ProductionCardGenerationStatus.Succeeded
        && providers.Length > 0
        && providers.All(provider => provider.IsConfiguredForLiveEndpoint || provider.EvidenceClassification == "LOCAL_CONFIGURED")
        && artifacts.Count > 0);
  }

  private static ProviderConfigurationEvidence ToConfigurationEvidence(
    string providerRole,
    GenerationProviderConfiguration configuration) =>
    new(
      providerRole,
      configuration.ProviderName,
      configuration.ModelId,
      configuration.Endpoint?.Host,
      string.IsNullOrWhiteSpace(configuration.ApiKey) ? "MISSING" : "REDACTED",
      (int)Math.Round(configuration.Timeout.TotalSeconds),
      configuration.MaxRetryAttempts,
      IsLiveConfigured(configuration),
      IsLiveConfigured(configuration)
        ? "LIVE_CONFIGURED_REDACTED"
        : IsLocalConfigured(configuration)
          ? "LOCAL_CONFIGURED"
        : "BLOCKED_OR_LOCAL");

  private static bool IsLiveConfigured(GenerationProviderConfiguration configuration) =>
    configuration.Endpoint is not null
    && !string.IsNullOrWhiteSpace(configuration.Endpoint.Host)
    && !GenerationProviderConfigurationValidator.IsLocalEndpoint(configuration.Endpoint)
    && !string.IsNullOrWhiteSpace(configuration.ApiKey);

  private static bool IsLocalConfigured(GenerationProviderConfiguration configuration) =>
    configuration.Endpoint is not null
    && !string.IsNullOrWhiteSpace(configuration.Endpoint.Host)
    && GenerationProviderConfigurationValidator.IsLocalEndpoint(configuration.Endpoint);

  private static IReadOnlyList<ProviderArtifactEvidence> CreateArtifacts(
    ProductionCardGenerationResult? generation)
  {
    if (generation is null)
    {
      return [];
    }

    var artifacts = new List<ProviderArtifactEvidence>();
    if (generation.CardVersion is not null)
    {
      artifacts.Add(new ProviderArtifactEvidence(
        "STRUCTURED_OUTPUT",
        generation.AuditLogEntry?.RawStructuredOutputRef ?? generation.CardVersion.CardVersionId,
        HashStableRef("STRUCTURED_OUTPUT", generation.CardVersion.CardId, generation.CardVersion.CardVersionId)));
    }

    if (generation.CatalogSnapshot is not null)
    {
      artifacts.Add(new ProviderArtifactEvidence(
        "CATALOG_SNAPSHOT",
        generation.CatalogSnapshot.SnapshotId,
        HashStableRef("CATALOG_SNAPSHOT", generation.CatalogSnapshot.SnapshotId, generation.CatalogSnapshot.RulesetProfileId)));
    }

    artifacts.AddRange(generation.AssetManifests
      .OrderBy(manifest => manifest.AssetManifestId, StringComparer.Ordinal)
      .Select(manifest => new ProviderArtifactEvidence(
        manifest.VisualAssetMode.ToString().ToUpperInvariant(),
        manifest.AssetManifestId,
        manifest.ContentHash)));

    return artifacts
      .OrderBy(artifact => artifact.ArtifactKind, StringComparer.Ordinal)
      .ThenBy(artifact => artifact.ArtifactRef, StringComparer.Ordinal)
      .ToArray();
  }

  private static string Role(int index) =>
    index switch
    {
      0 => "structured",
      1 => "image",
      2 => "animation",
      _ => $"provider-{index + 1}"
    };

  private static string HashStableRef(string kind, string primaryRef, string secondaryRef)
  {
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}|{primaryRef}|{secondaryRef}"));
    return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
  }
}

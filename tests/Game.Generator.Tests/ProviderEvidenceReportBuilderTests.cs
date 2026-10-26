using Game.Generator.Audit;
using Game.Generator.Generation;
using Game.Generator.Providers;
using Game.Generator.Validation;
using Game.Shared.Assets;
using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Generator.Tests;

public sealed class ProviderEvidenceReportBuilderTests
{
  private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-06-27T00:00:00Z");

  [Fact]
  public void BuildCreatesBlockedReportWithRedactedConfigurationAndDiagnostics()
  {
    var missing = new GenerationProviderConfiguration(
      "structured-real",
      ModelId: "",
      Endpoint: null,
      ApiKey: null,
      TimeSpan.FromSeconds(30),
      MaxRetryAttempts: 1);
    var configured = new GenerationProviderConfiguration(
      "image-real",
      "image-model",
      new Uri("https://provider.example.test/v1"),
      "super-secret-api-key",
      TimeSpan.FromSeconds(45),
      MaxRetryAttempts: 2);
    var harness = new RealProviderGenerationHarnessResult(
      ProductionCardGenerationStatus.Blocked,
      GenerationResult: null,
      [
        new ProductionCardGenerationDiagnostic(
          "PROVIDER_CONFIGURATION_MISSING",
          "providers.structured.endpoint",
          "structured",
          "Provider configuration is missing.")
      ],
      [missing.Redacted(), configured.Redacted()]);

    var report = new ProviderEvidenceReportBuilder().Build(new ProviderEvidenceReportRequest(
      "provider-evidence-001",
      FixedNow,
      harness));

    Assert.Equal("provider-evidence-001", report.ReportId);
    Assert.Equal(ProductionCardGenerationStatus.Blocked, report.Status);
    Assert.Empty(report.Artifacts);
    Assert.Contains(report.Diagnostics, diagnostic =>
      diagnostic.Contains("PROVIDER_CONFIGURATION_MISSING:providers.structured.endpoint:structured", StringComparison.Ordinal));
    Assert.Contains(report.Providers, provider =>
      provider.ProviderRole == "structured"
      && provider.ApiKeyStatus == "MISSING"
      && provider.EndpointHost is null);
    Assert.Contains(report.Providers, provider =>
      provider.ProviderRole == "image"
      && provider.ApiKeyStatus == "REDACTED"
      && provider.EndpointHost == "provider.example.test"
      && provider.IsConfiguredForLiveEndpoint
      && provider.EvidenceClassification == "LIVE_CONFIGURED_REDACTED");
    Assert.False(report.HasLiveProviderEvidence);
    Assert.False(report.HasConfiguredProviderEvidence);
    Assert.All(report.RedactedProviderRefs, providerRef =>
      Assert.DoesNotContain("super-secret-api-key", providerRef, StringComparison.OrdinalIgnoreCase));
    Assert.Contains(report.RedactedProviderRefs, providerRef =>
      providerRef.Contains("key:redacted", StringComparison.Ordinal));
  }

  [Fact]
  public void BuildCapturesConfiguredGenerationArtifactHashes()
  {
    var card = CreateCardVersion();
    var generation = new ProductionCardGenerationResult(
      ProductionCardGenerationStatus.Succeeded,
      card,
      new CatalogSnapshot(
        "catalog-provider-evidence",
        card.RulesetProfileId,
        card.RulesetProfileHash,
        FixedNow,
        [card]),
      [CreateAsset(card, VisualAssetMode.StaticImage), CreateAsset(card, VisualAssetMode.IdleAnimation)],
      CreateAudit(card),
      RepairAttemptRefs: [],
      Diagnostics: []);
    var configured = new GenerationProviderConfiguration(
      "real-provider",
      "model-v1",
      new Uri("https://provider.example.test/v1"),
      "secret-key",
      TimeSpan.FromSeconds(60),
      MaxRetryAttempts: 3);
    var harness = new RealProviderGenerationHarnessResult(
      ProductionCardGenerationStatus.Succeeded,
      generation,
      Diagnostics: [],
      [configured.Redacted(), configured.Redacted(), configured.Redacted()]);

    var report = new ProviderEvidenceReportBuilder().Build(new ProviderEvidenceReportRequest(
      "provider-evidence-002",
      FixedNow,
      harness));

    Assert.Equal(ProductionCardGenerationStatus.Succeeded, report.Status);
    Assert.True(report.HasLiveProviderEvidence);
    Assert.True(report.HasConfiguredProviderEvidence);
    Assert.Contains(report.Artifacts, artifact =>
      artifact.ArtifactKind == "STRUCTURED_OUTPUT"
      && artifact.ArtifactRef == "raw:provider-evidence"
      && artifact.ContentHash.StartsWith("sha256:", StringComparison.Ordinal));
    Assert.Contains(report.Artifacts, artifact =>
      artifact.ArtifactKind == "CATALOG_SNAPSHOT"
      && artifact.ArtifactRef == "catalog-provider-evidence"
      && artifact.ContentHash.StartsWith("sha256:", StringComparison.Ordinal));
    Assert.Contains(report.Artifacts, artifact =>
      artifact.ArtifactKind == "STATICIMAGE"
      && artifact.ArtifactRef == "asset-static"
      && artifact.ContentHash == "sha256:static");
    Assert.Contains(report.Artifacts, artifact =>
      artifact.ArtifactKind == "IDLEANIMATION"
      && artifact.ArtifactRef == "asset-idle"
      && artifact.ContentHash == "sha256:idle");
    Assert.All(report.RedactedProviderRefs, providerRef =>
      Assert.DoesNotContain("secret-key", providerRef, StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void BuildTreatsSuccessfulLocalProviderEvidenceAsConfiguredWithoutSecrets()
  {
    var card = CreateCardVersion();
    var generation = new ProductionCardGenerationResult(
      ProductionCardGenerationStatus.Succeeded,
      card,
      new CatalogSnapshot(
        "catalog-local-provider-evidence",
        card.RulesetProfileId,
        card.RulesetProfileHash,
        FixedNow,
        [card]),
      [CreateAsset(card, VisualAssetMode.StaticImage), CreateAsset(card, VisualAssetMode.IdleAnimation)],
      CreateAudit(card),
      RepairAttemptRefs: [],
      Diagnostics: []);
    var structured = new GenerationProviderConfiguration(
      "local-structured",
      "llama3.1:8b-instruct",
      new Uri("http://127.0.0.1:11434/v1/structured-card"),
      ApiKey: null,
      TimeSpan.FromSeconds(60),
      MaxRetryAttempts: 0);
    var image = new GenerationProviderConfiguration(
      "local-image",
      "sdxl-local",
      new Uri("http://localhost:7860/v1/assets"),
      ApiKey: null,
      TimeSpan.FromSeconds(120),
      MaxRetryAttempts: 0);
    var animation = new GenerationProviderConfiguration(
      "local-animation",
      "animatediff-local",
      new Uri("http://host.docker.internal:7860/v1/assets"),
      ApiKey: null,
      TimeSpan.FromSeconds(120),
      MaxRetryAttempts: 0);
    var harness = new RealProviderGenerationHarnessResult(
      ProductionCardGenerationStatus.Succeeded,
      generation,
      Diagnostics: [],
      [structured.Redacted(), image.Redacted(), animation.Redacted()]);

    var report = new ProviderEvidenceReportBuilder().Build(new ProviderEvidenceReportRequest(
      "provider-evidence-local-001",
      FixedNow,
      harness));

    Assert.Equal(ProductionCardGenerationStatus.Succeeded, report.Status);
    Assert.False(report.HasLiveProviderEvidence);
    Assert.True(report.HasConfiguredProviderEvidence);
    Assert.All(report.Providers, provider =>
    {
      Assert.Equal("MISSING", provider.ApiKeyStatus);
      Assert.False(provider.IsConfiguredForLiveEndpoint);
      Assert.Equal("LOCAL_CONFIGURED", provider.EvidenceClassification);
    });
    Assert.All(report.RedactedProviderRefs, providerRef =>
      Assert.Contains("key:missing", providerRef, StringComparison.Ordinal));
  }

  [Fact]
  public void ValidatorAcceptsLoopbackProvidersWithoutSecretsButRejectsExternalProvidersWithoutSecrets()
  {
    var validator = new GenerationProviderConfigurationValidator();
    var local = new GenerationProviderConfiguration(
      "local-structured",
      "llama3.1:8b-instruct",
      new Uri("http://127.0.0.1:11434/v1/structured-card"),
      ApiKey: null,
      TimeSpan.FromSeconds(30),
      MaxRetryAttempts: 0);
    var external = local with
    {
      ProviderName = "external-structured",
      Endpoint = new Uri("https://provider.example.test/v1/structured-card")
    };

    var localResult = validator.Validate(local);
    var externalResult = validator.Validate(external);

    Assert.True(localResult.IsConfigured);
    Assert.True(localResult.IsLocalConfigured);
    Assert.DoesNotContain("apiKey", localResult.MissingFields);
    Assert.False(externalResult.IsConfigured);
    Assert.False(externalResult.IsLocalConfigured);
    Assert.Contains("apiKey", externalResult.MissingFields);
  }

  private static CardVersion CreateCardVersion() =>
    new(
      "card_provider_evidence",
      "card_provider_evidence_v1",
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      "catalog-provider-evidence",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:profile",
      FixedNow,
      new CardDefinition(
        "card_provider_evidence",
        "card_provider_evidence_v1",
        "card-dsl-0.2.0",
        "engine-bootstrap-0.3.0",
        "primitive-registry-bootstrap-0.2.0",
        "ruleset_mvp_core_v1",
        "catalog-provider-evidence",
        new StaticCardData(
          "Provider Evidence",
          CardType.MinionCard,
          ["mage"],
          Rarity.Common,
          1,
          [],
          [],
          Collectible: true,
          ["mvp"]),
        new EntityDefinition(new MinionDefinition(1, 1, [], [], CanAttack: true), null, null, null, null, null),
        Effects: [],
        new CardTextGeneration("Provider evidence.", null),
        new BalanceMetadata(1, 0, []),
        new AssetMetadata("asset-static", "asset://provider-evidence/static", null),
        new AuthoringMetadata("test", FixedNow, "pipeline-v1", [])));

  private static AssetManifest CreateAsset(CardVersion card, VisualAssetMode mode)
  {
    var id = mode is VisualAssetMode.StaticImage ? "asset-static" : "asset-idle";
    var hash = mode is VisualAssetMode.StaticImage ? "sha256:static" : "sha256:idle";
    return new AssetManifest(
      id,
      "asset-manifest-bootstrap-0.1.0",
      card.CardId,
      card.CardVersionId,
      mode,
      AudioAssetMode.None,
      new VisualAssetManifest(
        $"visual-{id}",
        "visual-v1",
        $"asset://provider-evidence/{id}",
        mode is VisualAssetMode.StaticImage ? "png" : "gif",
        AssetModerationStatus.Passed,
        hash),
      AudioManifest: null,
      new AssetFallbackPolicy(AllowSilence: true, []),
      AssetModerationStatus.Passed,
      FixedNow,
      hash);
  }

  private static GenerationAuditLogEntry CreateAudit(CardVersion card) =>
    new(
      "generation-provider-evidence",
      FixedNow,
      "pipeline-v1",
      "context-v1",
      card.RulesetProfileId,
      card.RulesetProfileHash,
      "intent:provider-evidence",
      "raw:provider-evidence",
      "skeleton:provider-evidence",
      EffectSlotsRef: null,
      ExpandedEffectsRef: null,
      ValidationResults: [],
      RepairAttemptRefs: [],
      BalanceReportRef: null,
      SimulationReportRef: "simulation-report",
      ReplayReportRef: "replay-report",
      ImageAssetManifestRef: "asset-static",
      AnimationAssetManifestRef: "asset-idle",
      ProviderProvenanceRefs: ["provider:model:redacted"],
      PublicationRef: card.CardVersionId,
      GeneratedCardValidationDecision.Accepted);
}

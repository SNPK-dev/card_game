using Game.Generator.Generation;

namespace Game.Generator.Acceptance;

public sealed class ProductionAcceptanceReportBuilder
{
  public ProductionAcceptanceReport Build(ProductionAcceptanceReportRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.GeneratedCards);
    ArgumentNullException.ThrowIfNull(request.Decks);
    ArgumentNullException.ThrowIfNull(request.AssetManifests);
    ArgumentNullException.ThrowIfNull(request.Match);
    ArgumentNullException.ThrowIfNull(request.AuditEntries);
    ArgumentNullException.ThrowIfNull(request.ProviderHarnessResult);
    ArgumentNullException.ThrowIfNull(request.ResidualRisks);

    if (string.IsNullOrWhiteSpace(request.ReportId))
    {
      throw new ArgumentException("Acceptance report id is required.", nameof(request));
    }
    if (request.ReadinessEvidence is not null && request.ReadinessEvidenceReport is not null)
    {
      throw new ArgumentException(
        "Acceptance report request cannot contain both explicit readiness evidence and readiness evidence report.",
        nameof(request));
    }

    var auditsByPublication = request.AuditEntries
      .Where(entry => !string.IsNullOrWhiteSpace(entry.PublicationRef))
      .GroupBy(entry => entry.PublicationRef!, StringComparer.Ordinal)
      .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    var assetsByCardVersion = request.AssetManifests
      .GroupBy(manifest => manifest.CardVersionId, StringComparer.Ordinal)
      .ToDictionary(
        group => group.Key,
        group => group
          .OrderBy(manifest => manifest.AssetManifestId, StringComparer.Ordinal)
          .Select(manifest => manifest.AssetManifestId)
          .ToArray(),
        StringComparer.Ordinal);
    var cards = request.GeneratedCards
      .OrderBy(card => card.CardId, StringComparer.Ordinal)
      .ThenBy(card => card.CardVersionId, StringComparer.Ordinal)
      .Select(card =>
      {
        auditsByPublication.TryGetValue(card.CardVersionId, out var audit);
        assetsByCardVersion.TryGetValue(card.CardVersionId, out var assetRefs);
        return new AcceptedGeneratedCardEvidence(
          card.CardId,
          card.CardVersionId,
          audit?.GenerationId,
          audit is null ? null : $"generation-audit:{audit.GenerationId}",
          assetRefs ?? []);
      })
      .ToArray();

    var decks = request.Decks
      .OrderBy(deck => deck.DeckId, StringComparer.Ordinal)
      .Select(deck => new AcceptedDeckEvidence(
        deck.DeckId,
        deck.PlayerId,
        deck.Cards
          .Select(card => card.CardId)
          .Order(StringComparer.Ordinal)
          .ToArray()))
      .ToArray();

    var providerDiagnostics = request.ProviderHarnessResult.Diagnostics
      .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
      .ThenBy(diagnostic => diagnostic.Path, StringComparer.Ordinal)
      .Select(diagnostic => $"{diagnostic.Code}:{diagnostic.Path}:{diagnostic.Message}")
      .ToArray();
    var providerRefs = request.ProviderHarnessResult.RedactedProviderConfigurations
      .OrderBy(configuration => configuration.ProviderName, StringComparer.Ordinal)
      .ThenBy(configuration => configuration.ModelId, StringComparer.Ordinal)
      .Select(configuration => $"{configuration.ProviderName}:{configuration.ModelId}:{configuration.Endpoint?.ToString() ?? "missing-endpoint"}")
      .ToList();
    if (request.ProviderEvidenceReport is not null)
    {
      providerRefs.Add($"provider-evidence-report:{request.ProviderEvidenceReport.ReportId}");
    }

    var providerEvidence = new AcceptedProviderEvidence(
      request.ProviderHarnessResult.Status,
      providerDiagnostics,
      providerRefs.Order(StringComparer.Ordinal).ToArray(),
      request.ProviderEvidenceReport?.ReportId);
    var requestedReadinessEvidence = request.ReadinessEvidence
      ?? request.ReadinessEvidenceReport?.ReadinessEvidence
      ?? ProductionReadinessEvidence.Missing();
    var readinessEvidence = Normalize(requestedReadinessEvidence)
      with
      {
        EvidenceReportRef = request.ReadinessEvidenceReport?.ReportId ?? requestedReadinessEvidence.EvidenceReportRef
      };

    var isBlocked = request.ProviderHarnessResult.Status is not ProductionCardGenerationStatus.Succeeded
      || !request.Match.ReplayVerified
      || !readinessEvidence.IsAccepted
      || request.ResidualRisks.Count > 0;

    return new ProductionAcceptanceReport(
      request.ReportId,
      request.CreatedAt,
      isBlocked ? ProductionAcceptanceStatus.Blocked : ProductionAcceptanceStatus.Accepted,
      cards,
      decks,
      request.Match with
      {
        ReconstructionAssetRefs = request.Match.ReconstructionAssetRefs
          .Order(StringComparer.Ordinal)
          .ToArray(),
        EvidenceArtifacts = request.Match.EvidenceArtifacts
          .OrderBy(artifact => artifact.ArtifactKind, StringComparer.Ordinal)
          .ThenBy(artifact => artifact.ArtifactRef, StringComparer.Ordinal)
          .ToArray()
      },
      providerEvidence,
      readinessEvidence,
      request.ResidualRisks.Order(StringComparer.Ordinal).ToArray());
  }

  private static ProductionReadinessEvidence Normalize(ProductionReadinessEvidence evidence) =>
    new(
      evidence.Security.Normalize(),
      evidence.Recovery.Normalize(),
      evidence.Load.Normalize());
}

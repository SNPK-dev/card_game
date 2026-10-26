namespace Game.Generator.Acceptance;

public sealed record ProductionReadinessEvidenceReport(
  string ReportId,
  DateTimeOffset CreatedAt,
  ProductionReadinessEvidence ReadinessEvidence,
  IReadOnlyList<ProductionReadinessEvidenceArtifact> Artifacts,
  IReadOnlyList<string> Diagnostics)
{
  public bool IsAccepted => ReadinessEvidence.IsAccepted && Diagnostics.Count == 0;
}

public sealed record ProductionReadinessEvidenceArtifact(
  string GateId,
  string ArtifactKind,
  string ArtifactRef,
  string ContentHash,
  DateTimeOffset CreatedAt);

public sealed record ProductionReadinessEvidenceReportRequest(
  string ReportId,
  DateTimeOffset CreatedAt,
  IReadOnlyList<ProductionReadinessEvidenceArtifact> Artifacts);

public sealed class ProductionReadinessEvidenceReportBuilder
{
  private static readonly IReadOnlyDictionary<string, string> RequiredArtifactKinds = new Dictionary<string, string>(
    StringComparer.Ordinal)
  {
    ["L9_SECURITY"] = "SECURITY_ABUSE_REPORT",
    ["L10_RECOVERY"] = "RECOVERY_RESTART_REPORT",
    ["L10_LOAD"] = "LOAD_TEST_REPORT"
  };

  public ProductionReadinessEvidenceReport Build(ProductionReadinessEvidenceReportRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.Artifacts);
    if (string.IsNullOrWhiteSpace(request.ReportId))
    {
      throw new ArgumentException("Readiness evidence report id is required.", nameof(request));
    }

    var artifacts = request.Artifacts
      .OrderBy(artifact => artifact.GateId, StringComparer.Ordinal)
      .ThenBy(artifact => artifact.ArtifactKind, StringComparer.Ordinal)
      .ThenBy(artifact => artifact.ArtifactRef, StringComparer.Ordinal)
      .ToArray();
    var diagnostics = artifacts
      .SelectMany(ValidateArtifact)
      .Order(StringComparer.Ordinal)
      .ToArray();
    var readinessEvidence = new ProductionReadinessEvidence(
      BuildGate("L9_SECURITY", artifacts, diagnostics),
      BuildGate("L10_RECOVERY", artifacts, diagnostics),
      BuildGate("L10_LOAD", artifacts, diagnostics));

    return new ProductionReadinessEvidenceReport(
      request.ReportId,
      request.CreatedAt,
      readinessEvidence,
      artifacts,
      diagnostics);
  }

  private static AcceptanceGateEvidence BuildGate(
    string gateId,
    IReadOnlyList<ProductionReadinessEvidenceArtifact> artifacts,
    IReadOnlyList<string> allDiagnostics)
  {
    var expectedKind = RequiredArtifactKinds[gateId];
    var gateArtifacts = artifacts
      .Where(artifact => StringComparer.Ordinal.Equals(artifact.GateId, gateId))
      .ToArray();
    var gateDiagnostics = allDiagnostics
      .Where(diagnostic => diagnostic.StartsWith($"{gateId}:", StringComparison.Ordinal))
      .ToArray();
    var validRefs = gateArtifacts
      .Where(artifact =>
        StringComparer.Ordinal.Equals(artifact.ArtifactKind, expectedKind)
        && MatchEvidenceArtifactRefValidator.IsValid(artifact.ArtifactRef)
        && MatchEvidenceArtifactHashValidator.IsValid(artifact.ContentHash))
      .Select(artifact => artifact.ArtifactRef)
      .ToArray();

    if (validRefs.Length > 0 && gateDiagnostics.Length == 0)
    {
      return AcceptanceGateEvidence.Passed(gateId, validRefs);
    }

    var diagnostics = gateDiagnostics.Length > 0
      ? gateDiagnostics
      : [$"{gateId}:required artifact kind '{expectedKind}' is missing"];
    return AcceptanceGateEvidence.Blocked(gateId, diagnostics, validRefs);
  }

  private static IEnumerable<string> ValidateArtifact(ProductionReadinessEvidenceArtifact artifact)
  {
    if (string.IsNullOrWhiteSpace(artifact.GateId) || !RequiredArtifactKinds.ContainsKey(artifact.GateId))
    {
      yield return $"UNKNOWN_GATE:{artifact.GateId}:readiness evidence gate is not supported";
      yield break;
    }

    var expectedKind = RequiredArtifactKinds[artifact.GateId];
    if (!StringComparer.Ordinal.Equals(artifact.ArtifactKind, expectedKind))
    {
      yield return $"{artifact.GateId}:artifact kind '{artifact.ArtifactKind}' does not match required kind '{expectedKind}'";
    }

    if (!MatchEvidenceArtifactRefValidator.IsValid(artifact.ArtifactRef))
    {
      yield return $"{artifact.GateId}:invalid artifact ref '{artifact.ArtifactRef}'";
    }

    if (!MatchEvidenceArtifactHashValidator.IsValid(artifact.ContentHash))
    {
      yield return $"{artifact.GateId}:invalid content hash '{artifact.ContentHash}'";
    }
  }
}

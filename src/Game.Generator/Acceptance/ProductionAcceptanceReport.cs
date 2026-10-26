using Game.Generator.Generation;

namespace Game.Generator.Acceptance;

public sealed record ProductionAcceptanceReport(
  string ReportId,
  DateTimeOffset CreatedAt,
  ProductionAcceptanceStatus Status,
  IReadOnlyList<AcceptedGeneratedCardEvidence> GeneratedCards,
  IReadOnlyList<AcceptedDeckEvidence> Decks,
  AcceptedMatchEvidence Match,
  AcceptedProviderEvidence ProviderEvidence,
  ProductionReadinessEvidence ReadinessEvidence,
  IReadOnlyList<string> ResidualRisks);

public enum ProductionAcceptanceStatus
{
  Accepted,
  Blocked
}

public sealed record AcceptedGeneratedCardEvidence(
  string CardId,
  string CardVersionId,
  string? GenerationId,
  string? AuditRef,
  IReadOnlyList<string> AssetManifestRefs);

public sealed record AcceptedDeckEvidence(
  string DeckId,
  string PlayerId,
  IReadOnlyList<string> CardIds);

public sealed record AcceptedMatchEvidence(
  string MatchId,
  string SimulationId,
  string FinalStateHash,
  string TerminalReason,
  string? WinningPlayerId,
  int CommandCount,
  int EventCount,
  int ReplayRuns,
  bool ReplayVerified,
  int ReconstructionTurnCount,
  IReadOnlyList<string> ReconstructionAssetRefs,
  IReadOnlyList<ExternalMatchEvidenceArtifact> EvidenceArtifacts = null!)
{
  public IReadOnlyList<ExternalMatchEvidenceArtifact> EvidenceArtifacts { get; init; } = EvidenceArtifacts ?? [];
}

public sealed record ExternalMatchEvidenceArtifact(
  string ArtifactKind,
  string ArtifactRef,
  string ContentHash);

public sealed record AcceptedProviderEvidence(
  ProductionCardGenerationStatus Status,
  IReadOnlyList<string> Diagnostics,
  IReadOnlyList<string> RedactedProviderRefs,
  string? EvidenceReportRef = null);

public sealed record ProductionReadinessEvidence(
  AcceptanceGateEvidence Security,
  AcceptanceGateEvidence Recovery,
  AcceptanceGateEvidence Load)
{
  public string? EvidenceReportRef { get; init; }

  public bool IsAccepted => Security.IsAccepted && Recovery.IsAccepted && Load.IsAccepted;

  public static ProductionReadinessEvidence Missing() =>
    new(
      AcceptanceGateEvidence.Missing("L9_SECURITY"),
      AcceptanceGateEvidence.Missing("L10_RECOVERY"),
      AcceptanceGateEvidence.Missing("L10_LOAD"));
}

public sealed record AcceptanceGateEvidence(
  string GateId,
  AcceptanceGateEvidenceStatus Status,
  IReadOnlyList<string> EvidenceRefs,
  IReadOnlyList<string> Diagnostics)
{
  public bool IsAccepted =>
    Status is AcceptanceGateEvidenceStatus.Passed
    && EvidenceRefs.Count > 0
    && Diagnostics.Count == 0;

  public AcceptanceGateEvidence Normalize()
  {
    var evidenceRefs = EvidenceRefs
      .Where(evidenceRef => !string.IsNullOrWhiteSpace(evidenceRef))
      .Order(StringComparer.Ordinal)
      .ToArray();
    var diagnostics = Diagnostics
      .Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic))
      .Order(StringComparer.Ordinal)
      .ToArray();

    return Status is AcceptanceGateEvidenceStatus.Passed && evidenceRefs.Length == 0
      ? this with
      {
        Status = AcceptanceGateEvidenceStatus.Blocked,
        EvidenceRefs = evidenceRefs,
        Diagnostics = diagnostics
          .Append($"{GateId}:passed gate requires at least one evidence artifact ref")
          .Order(StringComparer.Ordinal)
          .ToArray()
      }
      : this with
      {
        EvidenceRefs = evidenceRefs,
        Diagnostics = diagnostics
      };
  }

  public static AcceptanceGateEvidence Passed(string gateId, IReadOnlyList<string> evidenceRefs) =>
    new AcceptanceGateEvidence(gateId, AcceptanceGateEvidenceStatus.Passed, evidenceRefs, []).Normalize();

  public static AcceptanceGateEvidence Blocked(
    string gateId,
    IReadOnlyList<string> diagnostics,
    IReadOnlyList<string>? evidenceRefs = null) =>
    new AcceptanceGateEvidence(gateId, AcceptanceGateEvidenceStatus.Blocked, evidenceRefs ?? [], diagnostics).Normalize();

  public static AcceptanceGateEvidence Missing(string gateId) =>
    Blocked(gateId, [$"{gateId}:evidence artifacts are required for production acceptance"]);
}

public enum AcceptanceGateEvidenceStatus
{
  Passed,
  Blocked
}

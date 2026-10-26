using Game.Generator.Acceptance;
using Xunit;

namespace Game.Generator.Tests;

public sealed class ProductionReadinessEvidenceReportBuilderTests
{
  private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-07-02T00:00:00Z");
  private const string ValidHash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

  [Fact]
  public void BuildAcceptsSecurityRecoveryAndLoadEvidenceArtifacts()
  {
    var report = new ProductionReadinessEvidenceReportBuilder().Build(new ProductionReadinessEvidenceReportRequest(
      "readiness-evidence-001",
      FixedNow,
      [
        Artifact("L10_LOAD", "LOAD_TEST_REPORT", "artifacts/load/load-acceptance.json"),
        Artifact("L9_SECURITY", "SECURITY_ABUSE_REPORT", "artifacts/security/e2e-009-abuse.json"),
        Artifact("L10_RECOVERY", "RECOVERY_RESTART_REPORT", "artifacts/recovery/e2e-007-restart.json")
      ]));

    Assert.True(report.IsAccepted);
    Assert.Empty(report.Diagnostics);
    Assert.Equal(["artifacts/security/e2e-009-abuse.json"], report.ReadinessEvidence.Security.EvidenceRefs);
    Assert.Equal(["artifacts/recovery/e2e-007-restart.json"], report.ReadinessEvidence.Recovery.EvidenceRefs);
    Assert.Equal(["artifacts/load/load-acceptance.json"], report.ReadinessEvidence.Load.EvidenceRefs);
  }

  [Fact]
  public void BuildBlocksMissingAndInvalidReadinessArtifacts()
  {
    var report = new ProductionReadinessEvidenceReportBuilder().Build(new ProductionReadinessEvidenceReportRequest(
      "readiness-evidence-002",
      FixedNow,
      [
        Artifact("L9_SECURITY", "SECURITY_ABUSE_REPORT", "../security/secrets.json"),
        Artifact("L10_RECOVERY", "RECOVERY_RESTART_REPORT", "artifacts/recovery/e2e-007-restart.json", "sha256:not-hex"),
        Artifact("L10_LOAD", "SECURITY_ABUSE_REPORT", "artifacts/load/load-acceptance.json"),
        Artifact("L11_FINAL", "FINAL_REPORT", "artifacts/final/report.json")
      ]));

    Assert.False(report.IsAccepted);
    Assert.Equal(AcceptanceGateEvidenceStatus.Blocked, report.ReadinessEvidence.Security.Status);
    Assert.Equal(AcceptanceGateEvidenceStatus.Blocked, report.ReadinessEvidence.Recovery.Status);
    Assert.Equal(AcceptanceGateEvidenceStatus.Blocked, report.ReadinessEvidence.Load.Status);
    Assert.Contains("L9_SECURITY:invalid artifact ref '../security/secrets.json'", report.Diagnostics);
    Assert.Contains("L10_RECOVERY:invalid content hash 'sha256:not-hex'", report.Diagnostics);
    Assert.Contains(
      "L10_LOAD:artifact kind 'SECURITY_ABUSE_REPORT' does not match required kind 'LOAD_TEST_REPORT'",
      report.Diagnostics);
    Assert.Contains("UNKNOWN_GATE:L11_FINAL:readiness evidence gate is not supported", report.Diagnostics);
  }

  private static ProductionReadinessEvidenceArtifact Artifact(
    string gateId,
    string artifactKind,
    string artifactRef,
    string contentHash = ValidHash) =>
    new(gateId, artifactKind, artifactRef, contentHash, FixedNow);
}

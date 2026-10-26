namespace Game.Application.Replay;

public sealed record ReplayVerificationResult(
  bool IsVerified,
  string? FinalStateHash,
  int DerivedEventCount,
  IReadOnlyList<string> RngRollRefs,
  ReplayRngTraceArtifact? RngTrace,
  IReadOnlyList<ReplayVerificationDiagnostic> Diagnostics,
  ReplayDebugTraceArtifact? DebugTrace = null)
{
  public static ReplayVerificationResult Verified(
    string finalStateHash,
    int derivedEventCount,
    IReadOnlyList<string>? rngRollRefs = null,
    ReplayRngTraceArtifact? rngTrace = null,
    ReplayDebugTraceArtifact? debugTrace = null) =>
    new(true, finalStateHash, derivedEventCount, rngRollRefs ?? [], rngTrace, [], debugTrace);

  public static ReplayVerificationResult Rejected(
    string? finalStateHash,
    int derivedEventCount,
    IReadOnlyList<ReplayVerificationDiagnostic> diagnostics,
    IReadOnlyList<string>? rngRollRefs = null,
    ReplayRngTraceArtifact? rngTrace = null,
    ReplayDebugTraceArtifact? debugTrace = null) =>
    new(false, finalStateHash, derivedEventCount, rngRollRefs ?? [], rngTrace, diagnostics, debugTrace);
}

namespace Game.Application.Capabilities;

public sealed record EngineCapabilityAuditReport(
  bool IsCompatible,
  string RegistryHash,
  IReadOnlyList<EngineCapabilityAuditDiagnostic> Diagnostics);

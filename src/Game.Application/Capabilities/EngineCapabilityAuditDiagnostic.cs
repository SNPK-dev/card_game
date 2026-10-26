namespace Game.Application.Capabilities;

public sealed record EngineCapabilityAuditDiagnostic(
  string Code,
  string Path,
  string Identifier,
  string Message);

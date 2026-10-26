namespace Game.Application.Capabilities;

public sealed record EngineCapabilityCompatibilityDiagnostic(
  string Code,
  string Path,
  string Identifier,
  string Message);

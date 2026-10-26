namespace Game.Application.Capabilities;

public sealed record EngineCapabilityCompatibilityResult(
  bool IsCompatible,
  IReadOnlyList<EngineCapabilityCompatibilityDiagnostic> Diagnostics)
{
  public static EngineCapabilityCompatibilityResult Compatible() => new(true, []);

  public static EngineCapabilityCompatibilityResult Rejected(
    IReadOnlyList<EngineCapabilityCompatibilityDiagnostic> diagnostics) =>
    new(false, diagnostics);
}

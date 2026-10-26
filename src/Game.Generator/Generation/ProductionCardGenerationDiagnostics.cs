namespace Game.Generator.Generation;

public sealed record ProductionCardGenerationDiagnostic(
  string Code,
  string Path,
  string Identifier,
  string Message);

public enum ProductionCardGenerationStatus
{
  Succeeded,
  Blocked,
  FailedValidation,
  FailedProvider,
  FailedPublication
}

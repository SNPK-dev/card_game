namespace Game.Generator.Publication;

public sealed record GeneratedCardPublicationDiagnostic(
  GeneratedCardPublicationDiagnosticCode Code,
  string Path,
  string Message);

public enum GeneratedCardPublicationDiagnosticCode
{
  ValidationReportGenerationIdMismatch,
  ValidationRejected,
  UnsupportedEngineCapability,
  CardIdRequired,
  CardVersionIdRequired,
  CatalogSnapshotIdRequired
}

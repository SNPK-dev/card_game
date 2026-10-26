namespace Game.Generator.Acceptance;

public sealed record ProductionAcceptanceReportStoreResult(
  bool Succeeded,
  string? Error)
{
  public static ProductionAcceptanceReportStoreResult Success { get; } = new(true, null);

  public static ProductionAcceptanceReportStoreResult DuplicateReportId(string reportId) =>
    new(false, $"Production acceptance report '{reportId}' already exists.");
}

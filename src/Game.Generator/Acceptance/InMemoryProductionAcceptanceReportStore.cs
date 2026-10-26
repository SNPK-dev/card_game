namespace Game.Generator.Acceptance;

public sealed class InMemoryProductionAcceptanceReportStore : IProductionAcceptanceReportStore
{
  private readonly List<ProductionAcceptanceReport> reports = [];
  private readonly HashSet<string> reportIds = new(StringComparer.Ordinal);

  public ProductionAcceptanceReport? Latest => reports.Count == 0 ? null : reports[^1];

  public IReadOnlyList<ProductionAcceptanceReport> Reports => reports.ToArray();

  public ProductionAcceptanceReportStoreResult Store(ProductionAcceptanceReport report)
  {
    ArgumentNullException.ThrowIfNull(report);

    if (!reportIds.Add(report.ReportId))
    {
      return ProductionAcceptanceReportStoreResult.DuplicateReportId(report.ReportId);
    }

    reports.Add(report);
    return ProductionAcceptanceReportStoreResult.Success;
  }

  public ProductionAcceptanceReport? FindById(string reportId) =>
    reports.FirstOrDefault(report => string.Equals(report.ReportId, reportId, StringComparison.Ordinal));
}

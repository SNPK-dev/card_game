using Game.Generator.Acceptance;

namespace Game.Infrastructure.Storage;

public sealed class JsonFileProductionAcceptanceReportStore : IProductionAcceptanceReportStore
{
  private readonly JsonFileCollectionStore<ProductionAcceptanceReport> store;

  public JsonFileProductionAcceptanceReportStore(string rootDirectory)
  {
    store = new JsonFileCollectionStore<ProductionAcceptanceReport>(
      Path.Combine(rootDirectory, "production-acceptance-reports.json"),
      report => report.ReportId);
  }

  public ProductionAcceptanceReport? Latest => store.Latest;

  public IReadOnlyList<ProductionAcceptanceReport> Reports => store.Items;

  public ProductionAcceptanceReportStoreResult Store(ProductionAcceptanceReport report) =>
    store.TryStore(report)
      ? ProductionAcceptanceReportStoreResult.Success
      : ProductionAcceptanceReportStoreResult.DuplicateReportId(report.ReportId);

  public ProductionAcceptanceReport? FindById(string reportId) => store.FindById(reportId);
}

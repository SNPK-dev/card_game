namespace Game.Generator.Acceptance;

public interface IProductionAcceptanceReportStore
{
  ProductionAcceptanceReport? Latest { get; }

  IReadOnlyList<ProductionAcceptanceReport> Reports { get; }

  ProductionAcceptanceReportStoreResult Store(ProductionAcceptanceReport report);

  ProductionAcceptanceReport? FindById(string reportId);
}

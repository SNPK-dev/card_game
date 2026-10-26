using Game.Shared.Catalog;

namespace Game.Application.Catalog;

public interface ICatalogSnapshotStore
{
  CatalogSnapshotStoreResult Store(CatalogSnapshot snapshot);

  CatalogSnapshot? FindById(string snapshotId);

  CatalogSnapshot? Latest { get; }

  IReadOnlyList<CatalogSnapshot> Snapshots { get; }
}

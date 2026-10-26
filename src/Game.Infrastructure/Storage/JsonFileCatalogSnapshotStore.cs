using Game.Application.Catalog;
using Game.Shared.Catalog;

namespace Game.Infrastructure.Storage;

public sealed class JsonFileCatalogSnapshotStore : ICatalogSnapshotStore
{
  private readonly JsonFileCollectionStore<CatalogSnapshot> store;

  public JsonFileCatalogSnapshotStore(string rootDirectory)
  {
    store = new JsonFileCollectionStore<CatalogSnapshot>(
      Path.Combine(rootDirectory, "catalog-snapshots.json"),
      snapshot => snapshot.SnapshotId);
  }

  public CatalogSnapshot? Latest => store.Latest;

  public IReadOnlyList<CatalogSnapshot> Snapshots => store.Items;

  public CatalogSnapshotStoreResult Store(CatalogSnapshot snapshot) =>
    store.TryStore(snapshot)
      ? CatalogSnapshotStoreResult.Success
      : CatalogSnapshotStoreResult.DuplicateSnapshotId(snapshot.SnapshotId);

  public CatalogSnapshot? FindById(string snapshotId) => store.FindById(snapshotId);
}

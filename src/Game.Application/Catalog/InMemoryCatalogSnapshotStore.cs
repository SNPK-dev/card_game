using Game.Shared.Catalog;

namespace Game.Application.Catalog;

public sealed class InMemoryCatalogSnapshotStore : ICatalogSnapshotStore
{
  private readonly List<CatalogSnapshot> snapshots = [];
  private readonly HashSet<string> snapshotIds = new(StringComparer.Ordinal);

  public CatalogSnapshot? Latest => snapshots.Count == 0 ? null : snapshots[^1];

  public IReadOnlyList<CatalogSnapshot> Snapshots => snapshots.ToArray();

  public CatalogSnapshotStoreResult Store(CatalogSnapshot snapshot)
  {
    ArgumentNullException.ThrowIfNull(snapshot);

    if (!snapshotIds.Add(snapshot.SnapshotId))
    {
      return CatalogSnapshotStoreResult.DuplicateSnapshotId(snapshot.SnapshotId);
    }

    snapshots.Add(snapshot);
    return CatalogSnapshotStoreResult.Success;
  }

  public CatalogSnapshot? FindById(string snapshotId) =>
    snapshots.FirstOrDefault(snapshot => string.Equals(snapshot.SnapshotId, snapshotId, StringComparison.Ordinal));
}

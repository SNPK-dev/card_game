namespace Game.Application.Catalog;

public sealed record CatalogSnapshotStoreResult(
  bool Succeeded,
  string? Error)
{
  public static CatalogSnapshotStoreResult Success { get; } = new(true, null);

  public static CatalogSnapshotStoreResult DuplicateSnapshotId(string snapshotId) =>
    new(false, $"Catalog snapshot '{snapshotId}' already exists.");
}

using Game.Shared.Catalog;

namespace Game.Application.Catalog;

public sealed class CatalogSnapshotQueryService(ICatalogSnapshotStore store)
{
  public const string CanonicalStarterSnapshotId = "catalog-unity-test-card-pack-v1";
  public CatalogSnapshot? GetLatestSnapshot() => store.Latest;

  public CatalogSnapshot? GetSnapshot(string snapshotId) => store.FindById(snapshotId);

  public IReadOnlyList<CatalogSnapshot> GetSnapshots() => store.Snapshots;

  public CardVersion? FindCardVersion(string cardVersionId) => store.Snapshots.Reverse()
    .SelectMany(snapshot=>snapshot.Cards)
    .FirstOrDefault(card=>string.Equals(card.CardVersionId,cardVersionId,StringComparison.Ordinal));

  public CatalogSnapshot? FindSnapshotContainingVersions(IEnumerable<string> cardVersionIds)
  {
    var required=cardVersionIds.ToHashSet(StringComparer.Ordinal);
    return store.Snapshots.Reverse().FirstOrDefault(snapshot=>required.IsSubsetOf(snapshot.Cards.Select(card=>card.CardVersionId)));
  }

  public CatalogSnapshot? GetCanonicalStarterSnapshot() =>
    store.FindById(CanonicalStarterSnapshotId)
    ?? store.Snapshots.LastOrDefault(snapshot => snapshot.Cards.Count >= 15 &&
      snapshot.Cards.Count(card => card.Definition.Effects.Count > 0 || !string.IsNullOrWhiteSpace(card.Definition.CardText.RulesText)) >= 10)
    ?? store.Latest;
}

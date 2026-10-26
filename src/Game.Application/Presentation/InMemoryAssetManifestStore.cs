using Game.Shared.Assets;

namespace Game.Application.Presentation;

public sealed class InMemoryAssetManifestStore : IAssetManifestStore
{
  private readonly List<AssetManifest> manifests = [];
  private readonly HashSet<string> manifestIds = new(StringComparer.Ordinal);

  public AssetManifest? Latest => manifests.Count == 0 ? null : manifests[^1];

  public IReadOnlyList<AssetManifest> Manifests => manifests.ToArray();

  public AssetManifestStoreResult Store(AssetManifest manifest)
  {
    ArgumentNullException.ThrowIfNull(manifest);

    if (!manifestIds.Add(manifest.AssetManifestId))
    {
      return AssetManifestStoreResult.DuplicateManifestId(manifest.AssetManifestId);
    }

    manifests.Add(manifest);
    return AssetManifestStoreResult.Success;
  }

  public AssetManifest? FindById(string assetManifestId) =>
    manifests.FirstOrDefault(manifest =>
      string.Equals(manifest.AssetManifestId, assetManifestId, StringComparison.Ordinal));
}

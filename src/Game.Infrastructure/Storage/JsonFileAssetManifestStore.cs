using Game.Application.Presentation;
using Game.Shared.Assets;

namespace Game.Infrastructure.Storage;

public sealed class JsonFileAssetManifestStore : IAssetManifestStore
{
  private readonly JsonFileCollectionStore<AssetManifest> store;

  public JsonFileAssetManifestStore(string rootDirectory)
  {
    store = new JsonFileCollectionStore<AssetManifest>(
      Path.Combine(rootDirectory, "asset-manifests.json"),
      manifest => manifest.AssetManifestId);
  }

  public AssetManifest? Latest => store.Latest;

  public IReadOnlyList<AssetManifest> Manifests => store.Items;

  public AssetManifestStoreResult Store(AssetManifest manifest) =>
    store.TryStore(manifest)
      ? AssetManifestStoreResult.Success
      : AssetManifestStoreResult.DuplicateManifestId(manifest.AssetManifestId);

  public AssetManifest? FindById(string assetManifestId) => store.FindById(assetManifestId);
}

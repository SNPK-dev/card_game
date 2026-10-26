using Game.Shared.Assets;

namespace Game.Application.Presentation;

public sealed class AssetManifestQueryService(IAssetManifestStore store)
{
  public AssetManifest? GetLatestManifest() => store.Latest;

  public AssetManifest? GetManifest(string assetManifestId) => store.FindById(assetManifestId);
}

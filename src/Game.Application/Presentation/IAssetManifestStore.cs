using Game.Shared.Assets;

namespace Game.Application.Presentation;

public interface IAssetManifestStore
{
  AssetManifestStoreResult Store(AssetManifest manifest);

  AssetManifest? FindById(string assetManifestId);

  AssetManifest? Latest { get; }

  IReadOnlyList<AssetManifest> Manifests { get; }
}

using Game.Shared.Assets;

namespace Game.Application.Presentation;

public interface IPresentationCatalogHasher
{
  string HashManifest(AssetManifest manifest);
}

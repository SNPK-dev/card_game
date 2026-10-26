using Game.Shared.Assets;
using Game.Shared.Engine;

namespace Game.Application.Presentation;

public interface IPresentationAssetCompatibilityValidator
{
  PresentationAssetCompatibilityResult Validate(
    AssetManifest manifest,
    EngineCapabilityRegistry registry);
}

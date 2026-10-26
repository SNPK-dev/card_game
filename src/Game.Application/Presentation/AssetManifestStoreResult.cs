namespace Game.Application.Presentation;

public sealed record AssetManifestStoreResult(
  bool Succeeded,
  string? Error)
{
  public static AssetManifestStoreResult Success { get; } = new(true, null);

  public static AssetManifestStoreResult DuplicateManifestId(string assetManifestId) =>
    new(false, $"Asset manifest '{assetManifestId}' already exists.");
}

using Game.Application.Presentation;
using Game.Shared.Assets;
using Xunit;

namespace Game.Application.Tests;

public sealed class AssetManifestStoreTests
{
  [Fact]
  public void StorePreservesManifestsAndReturnsLatestByInsertionOrder()
  {
    var first = CreateManifest("asset-alpha");
    var second = CreateManifest("asset-beta");
    var store = new InMemoryAssetManifestStore();
    var query = new AssetManifestQueryService(store);

    var firstResult = store.Store(first);
    var secondResult = store.Store(second);

    Assert.True(firstResult.Succeeded);
    Assert.True(secondResult.Succeeded);
    Assert.Equal(["asset-alpha", "asset-beta"], store.Manifests.Select(manifest => manifest.AssetManifestId));
    Assert.Same(second, store.Latest);
    Assert.Same(second, query.GetLatestManifest());
    Assert.Same(first, query.GetManifest("asset-alpha"));
  }

  [Fact]
  public void StoreRejectsDuplicateManifestIdAndPreservesOriginal()
  {
    var original = CreateManifest("asset-alpha", contentHash: "sha256:original");
    var duplicate = CreateManifest("asset-alpha", contentHash: "sha256:duplicate");
    var store = new InMemoryAssetManifestStore();

    var firstResult = store.Store(original);
    var duplicateResult = store.Store(duplicate);

    Assert.True(firstResult.Succeeded);
    Assert.False(duplicateResult.Succeeded);
    Assert.Contains("asset-alpha", duplicateResult.Error, StringComparison.Ordinal);
    Assert.Single(store.Manifests);
    Assert.Same(original, store.FindById("asset-alpha"));
    Assert.Equal("sha256:original", store.FindById("asset-alpha")?.ContentHash);
  }

  private static AssetManifest CreateManifest(
    string assetManifestId,
    string contentHash = "sha256:asset-root") =>
    new(
      assetManifestId,
      "asset-manifest-bootstrap-0.1.0",
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      VisualAssetMode.None,
      AudioAssetMode.None,
      null,
      null,
      new AssetFallbackPolicy(true, []),
      AssetModerationStatus.NotRequired,
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      contentHash);
}

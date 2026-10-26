using Game.Generator.Acceptance;

namespace Game.Infrastructure.Storage;

public sealed class JsonFileMatchEvidenceArtifactStore : IMatchEvidenceArtifactStore
{
  private readonly JsonFileCollectionStore<MatchEvidenceArtifact> store;

  public JsonFileMatchEvidenceArtifactStore(string rootDirectory)
  {
    store = new JsonFileCollectionStore<MatchEvidenceArtifact>(
      Path.Combine(rootDirectory, "match-evidence-artifacts.json"),
      artifact => artifact.ArtifactRef);
  }

  public IReadOnlyList<MatchEvidenceArtifact> Artifacts => store.Items;

  public MatchEvidenceArtifactStoreResult Store(MatchEvidenceArtifact artifact)
  {
    ArgumentNullException.ThrowIfNull(artifact);
    if (!MatchEvidenceArtifactRefValidator.IsValid(artifact.ArtifactRef))
    {
      return MatchEvidenceArtifactStoreResult.InvalidArtifactRef(artifact.ArtifactRef);
    }

    if (!MatchEvidenceArtifactHashValidator.IsValid(artifact.ContentHash))
    {
      return MatchEvidenceArtifactStoreResult.InvalidContentHash(artifact.ContentHash);
    }

    return store.TryStore(artifact)
      ? MatchEvidenceArtifactStoreResult.Success
      : MatchEvidenceArtifactStoreResult.Duplicate(artifact.ArtifactRef);
  }

  public MatchEvidenceArtifact? FindByRef(string artifactRef) => store.FindById(artifactRef);

  public IReadOnlyList<MatchEvidenceArtifact> FindByMatchId(string matchId) =>
    store.Items
      .Where(artifact => StringComparer.Ordinal.Equals(artifact.MatchId, matchId))
      .OrderBy(artifact => artifact.ArtifactKind, StringComparer.Ordinal)
      .ThenBy(artifact => artifact.ArtifactRef, StringComparer.Ordinal)
      .ToArray();
}

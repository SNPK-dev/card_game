namespace Game.Generator.Acceptance;

public sealed record MatchEvidenceArtifact(
  string ArtifactRef,
  string MatchId,
  string ArtifactKind,
  string ContentHash,
  DateTimeOffset CreatedAt);

public sealed record MatchEvidenceArtifactStoreResult(bool Succeeded, string? ErrorCode)
{
  public static MatchEvidenceArtifactStoreResult Success { get; } = new(true, null);

  public static MatchEvidenceArtifactStoreResult Duplicate(string artifactRef) =>
    new(false, $"DUPLICATE_MATCH_EVIDENCE:{artifactRef}");

  public static MatchEvidenceArtifactStoreResult InvalidArtifactRef(string artifactRef) =>
    new(false, $"INVALID_MATCH_EVIDENCE_ARTIFACT_REF:{artifactRef}");

  public static MatchEvidenceArtifactStoreResult InvalidContentHash(string contentHash) =>
    new(false, $"INVALID_MATCH_EVIDENCE_CONTENT_HASH:{contentHash}");
}

public static class MatchEvidenceArtifactRefValidator
{
  public static bool IsValid(string? artifactRef)
  {
    if (string.IsNullOrWhiteSpace(artifactRef)
      || artifactRef.Contains('\\', StringComparison.Ordinal)
      || artifactRef.Contains(':', StringComparison.Ordinal)
      || Path.IsPathRooted(artifactRef))
    {
      return false;
    }

    var segments = artifactRef.Split('/');
    return segments.Length > 0
      && segments.All(segment =>
        !string.IsNullOrWhiteSpace(segment)
        && !StringComparer.Ordinal.Equals(segment, ".")
        && !StringComparer.Ordinal.Equals(segment, ".."));
  }
}

public static class MatchEvidenceArtifactHashValidator
{
  public static bool IsValid(string? contentHash)
  {
    const string prefix = "sha256:";
    return !string.IsNullOrWhiteSpace(contentHash)
      && contentHash.Length == prefix.Length + 64
      && contentHash.StartsWith(prefix, StringComparison.Ordinal)
      && contentHash[prefix.Length..].All(IsLowerHex);
  }

  private static bool IsLowerHex(char value) =>
    value is >= '0' and <= '9' or >= 'a' and <= 'f';
}

public interface IMatchEvidenceArtifactStore
{
  IReadOnlyList<MatchEvidenceArtifact> Artifacts { get; }

  MatchEvidenceArtifactStoreResult Store(MatchEvidenceArtifact artifact);

  MatchEvidenceArtifact? FindByRef(string artifactRef);

  IReadOnlyList<MatchEvidenceArtifact> FindByMatchId(string matchId);
}

public sealed class InMemoryMatchEvidenceArtifactStore : IMatchEvidenceArtifactStore
{
  private readonly List<MatchEvidenceArtifact> artifacts = [];

  public IReadOnlyList<MatchEvidenceArtifact> Artifacts => artifacts.ToArray();

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

    if (artifacts.Any(candidate => StringComparer.Ordinal.Equals(candidate.ArtifactRef, artifact.ArtifactRef)))
    {
      return MatchEvidenceArtifactStoreResult.Duplicate(artifact.ArtifactRef);
    }

    artifacts.Add(artifact);
    return MatchEvidenceArtifactStoreResult.Success;
  }

  public MatchEvidenceArtifact? FindByRef(string artifactRef) =>
    artifacts.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.ArtifactRef, artifactRef));

  public IReadOnlyList<MatchEvidenceArtifact> FindByMatchId(string matchId) =>
    artifacts
      .Where(candidate => StringComparer.Ordinal.Equals(candidate.MatchId, matchId))
      .OrderBy(candidate => candidate.ArtifactKind, StringComparer.Ordinal)
      .ThenBy(candidate => candidate.ArtifactRef, StringComparer.Ordinal)
      .ToArray();
}

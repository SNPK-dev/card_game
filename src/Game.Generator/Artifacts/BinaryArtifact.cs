namespace Game.Generator.Artifacts;

public sealed record BinaryArtifact(
  string ArtifactRef,
  string ContentHash,
  string MediaType,
  long ByteLength,
  DateTimeOffset CreatedAt);

public sealed record BinaryArtifactStoreResult(
  bool Succeeded,
  string? ErrorCode)
{
  public static BinaryArtifactStoreResult Success { get; } = new(true, null);

  public static BinaryArtifactStoreResult Duplicate(string artifactRef) =>
    new(false, $"DUPLICATE_ARTIFACT:{artifactRef}");

  public static BinaryArtifactStoreResult HashMismatch(string expected, string actual) =>
    new(false, $"CONTENT_HASH_MISMATCH:{expected}:{actual}");
}

public interface IBinaryArtifactStore
{
  IReadOnlyList<BinaryArtifact> Artifacts { get; }

  BinaryArtifact? FindByRef(string artifactRef);

  BinaryArtifactStoreResult Store(BinaryArtifact artifact, ReadOnlySpan<byte> content);

  byte[]? ReadBytes(string artifactRef);
}

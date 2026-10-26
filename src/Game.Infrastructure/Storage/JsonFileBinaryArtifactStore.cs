using System.Security.Cryptography;
using Game.Generator.Artifacts;

namespace Game.Infrastructure.Storage;

public sealed class JsonFileBinaryArtifactStore : IBinaryArtifactStore
{
  private readonly JsonFileCollectionStore<BinaryArtifact> metadataStore;
  private readonly string binaryDirectory;

  public JsonFileBinaryArtifactStore(string rootDirectory)
  {
    metadataStore = new JsonFileCollectionStore<BinaryArtifact>(
      Path.Combine(rootDirectory, "binary-artifacts", "metadata.json"),
      artifact => artifact.ArtifactRef);
    binaryDirectory = Path.Combine(rootDirectory, "binary-artifacts", "blobs");
  }

  public IReadOnlyList<BinaryArtifact> Artifacts => metadataStore.Items;

  public BinaryArtifact? FindByRef(string artifactRef) => metadataStore.FindById(artifactRef);

  public BinaryArtifactStoreResult Store(BinaryArtifact artifact, ReadOnlySpan<byte> content)
  {
    ArgumentNullException.ThrowIfNull(artifact);
    var actualHash = ComputeHash(content);
    if (!StringComparer.Ordinal.Equals(artifact.ContentHash, actualHash))
    {
      return BinaryArtifactStoreResult.HashMismatch(artifact.ContentHash, actualHash);
    }

    if (FindByRef(artifact.ArtifactRef) is not null)
    {
      return BinaryArtifactStoreResult.Duplicate(artifact.ArtifactRef);
    }

    Directory.CreateDirectory(binaryDirectory);
    File.WriteAllBytes(Path.Combine(binaryDirectory, SafeFileName(artifact.ArtifactRef)), content.ToArray());
    return metadataStore.TryStore(artifact) ? BinaryArtifactStoreResult.Success : BinaryArtifactStoreResult.Duplicate(artifact.ArtifactRef);
  }

  public byte[]? ReadBytes(string artifactRef)
  {
    if (FindByRef(artifactRef) is null)
    {
      return null;
    }

    var path = Path.Combine(binaryDirectory, SafeFileName(artifactRef));
    return File.Exists(path) ? File.ReadAllBytes(path) : null;
  }

  public static string ComputeHash(ReadOnlySpan<byte> content) =>
    $"sha256:{Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()}";

  private static string SafeFileName(string artifactRef)
  {
    foreach (var invalid in Path.GetInvalidFileNameChars())
    {
      artifactRef = artifactRef.Replace(invalid, '_');
    }

    return artifactRef.Replace(':', '_');
  }
}

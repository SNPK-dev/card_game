namespace Game.Generator.Audit;

public sealed class InMemoryGenerationAuditLogStore : IGenerationAuditLogStore
{
  private readonly List<GenerationAuditLogEntry> entries = [];
  private readonly HashSet<string> generationIds = new(StringComparer.Ordinal);

  public IReadOnlyList<GenerationAuditLogEntry> Entries => entries.ToArray();

  public GenerationAuditLogStoreAppendResult Append(GenerationAuditLogEntry entry)
  {
    ArgumentNullException.ThrowIfNull(entry);

    if (!generationIds.Add(entry.GenerationId))
    {
      return GenerationAuditLogStoreAppendResult.DuplicateGenerationId(entry.GenerationId);
    }

    entries.Add(entry);
    return GenerationAuditLogStoreAppendResult.Success;
  }

  public GenerationAuditLogEntry? FindByGenerationId(string generationId) =>
    entries.FirstOrDefault(entry => string.Equals(entry.GenerationId, generationId, StringComparison.Ordinal));
}

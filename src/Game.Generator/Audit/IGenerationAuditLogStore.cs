namespace Game.Generator.Audit;

public interface IGenerationAuditLogStore
{
  GenerationAuditLogStoreAppendResult Append(GenerationAuditLogEntry entry);

  GenerationAuditLogEntry? FindByGenerationId(string generationId);

  IReadOnlyList<GenerationAuditLogEntry> Entries { get; }
}

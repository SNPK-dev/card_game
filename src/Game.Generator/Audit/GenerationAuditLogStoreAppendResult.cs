namespace Game.Generator.Audit;

public sealed record GenerationAuditLogStoreAppendResult(
  bool Succeeded,
  string? Error)
{
  public static GenerationAuditLogStoreAppendResult Success { get; } = new(true, null);

  public static GenerationAuditLogStoreAppendResult DuplicateGenerationId(string generationId) =>
    new(false, $"Generation id '{generationId}' already exists in the audit log.");
}

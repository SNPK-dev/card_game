namespace Game.Generator.Validation;

public sealed record GeneratedCardValidationResult(
  bool IsValid,
  IReadOnlyList<GeneratedCardValidationDiagnostic> Diagnostics)
{
  public static GeneratedCardValidationResult Valid { get; } = new(true, []);

  public static GeneratedCardValidationResult Invalid(
    IReadOnlyList<GeneratedCardValidationDiagnostic> diagnostics) =>
    new(false, diagnostics);
}

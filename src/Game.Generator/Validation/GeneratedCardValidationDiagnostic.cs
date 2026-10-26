namespace Game.Generator.Validation;

public sealed record GeneratedCardValidationDiagnostic(
  GeneratedCardValidationCode Code,
  GeneratedCardValidationSeverity Severity,
  string Path,
  string ActualValue,
  string Message);

namespace Game.Domain.Effects;

public sealed record EffectActionCompilationDiagnostic(
  EffectActionCompilationDiagnosticCode Code,
  string Message,
  string? EffectId = null,
  string? ActionId = null);

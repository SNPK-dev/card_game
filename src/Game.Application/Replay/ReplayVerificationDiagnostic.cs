namespace Game.Application.Replay;

public sealed record ReplayVerificationDiagnostic(
  string Code,
  string Path,
  string Identifier,
  string Message);

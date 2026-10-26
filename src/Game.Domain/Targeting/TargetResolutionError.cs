namespace Game.Domain.Targeting;

public sealed record TargetResolutionError(
  TargetResolutionErrorCode Code,
  string Message,
  string? TargetId = null);

namespace Game.Domain.State;

public sealed record PlayerQuest(
  string QuestId,
  string SourceCardInstanceId,
  string ControllerPlayerId,
  string ProgressEventType,
  int Progress,
  int RequiredProgress,
  bool IsCompleted,
  string? RewardEffectId = null,
  string? SourceCardVersion = null);

namespace Game.Domain.Events;

public sealed record EventQueueOptions(int MaxEventDepth, int MaxQueuedEvents)
{
  public static EventQueueOptions MvpDefaults { get; } = new(64, 512);
}

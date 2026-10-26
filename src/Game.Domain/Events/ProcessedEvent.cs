namespace Game.Domain.Events;

public sealed record ProcessedEvent(int EventSeq, QueuedEvent Event);

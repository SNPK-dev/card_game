namespace Game.Domain.Events;

public sealed record DerivedEvent(string Kind, int FromRevision, int ToRevision);

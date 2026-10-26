using Game.Shared.Events;

namespace Game.Domain.Triggers;

public sealed record TriggerResolutionRequest(
  IReadOnlyList<CommittedEventRecord> Events,
  IReadOnlyList<TriggerRegistration> Triggers);

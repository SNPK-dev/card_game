using Game.Shared.Dsl;

namespace Game.Domain.Triggers;

public sealed record TriggerRegistration(
  string TriggerId,
  string EffectId,
  TriggerDefinition Trigger,
  string ControllerPlayerId,
  string SourceRef,
  IReadOnlyList<string> CanonicalOrderingKeys);

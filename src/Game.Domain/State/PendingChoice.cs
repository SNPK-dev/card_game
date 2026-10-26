namespace Game.Domain.State;

public sealed record ChoiceOption(
  string OptionId,
  string ItemKind,
  string ItemId);

public sealed record PendingChoice(
  string ChoiceId,
  string PlayerId,
  string ChoiceType,
  string VisibilityPolicy,
  string ResultZone,
  string SourceRef,
  IReadOnlyList<ChoiceOption> Options);

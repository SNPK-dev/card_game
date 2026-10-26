using System.Text.Json.Serialization;
using Game.Shared.Primitives;

namespace Game.Shared.Dsl;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "actionType")]
[JsonDerivedType(typeof(DealDamageActionDefinition), "DEAL_DAMAGE")]
[JsonDerivedType(typeof(DamageDistributionActionDefinition), "DAMAGE_DISTRIBUTION")]
[JsonDerivedType(typeof(HealActionDefinition), "HEAL")]
[JsonDerivedType(typeof(FullyHealActionDefinition), "FULLY_HEAL")]
[JsonDerivedType(typeof(DrawCardActionDefinition), "DRAW_CARD")]
[JsonDerivedType(typeof(DiscardCardActionDefinition), "DISCARD_CARD")]
[JsonDerivedType(typeof(SummonActionDefinition), "SUMMON")]
[JsonDerivedType(typeof(DestroyActionDefinition), "DESTROY")]
[JsonDerivedType(typeof(BatchDestroyActionDefinition), "BATCH_DESTROY")]
[JsonDerivedType(typeof(RandomSelectActionDefinition), "RANDOM_SELECT")]
[JsonDerivedType(typeof(MoveToZoneActionDefinition), "MOVE_TO_ZONE")]
[JsonDerivedType(typeof(AddCardToHandActionDefinition), "ADD_CARD_TO_HAND")]
[JsonDerivedType(typeof(ReturnToHandActionDefinition), "RETURN_TO_HAND")]
[JsonDerivedType(typeof(ApplyModifierActionDefinition), "APPLY_MODIFIER")]
[JsonDerivedType(typeof(ApplyAuraActionDefinition), "APPLY_AURA")]
[JsonDerivedType(typeof(PreventDamageActionDefinition), "PREVENT_DAMAGE")]
[JsonDerivedType(typeof(ReplaceEventActionDefinition), "REPLACE_EVENT")]
[JsonDerivedType(typeof(ShuffleIntoDeckActionDefinition), "SHUFFLE_INTO_DECK")]
[JsonDerivedType(typeof(DiscoverActionDefinition), "DISCOVER")]
[JsonDerivedType(typeof(ResurrectActionDefinition), "RESURRECT")]
[JsonDerivedType(typeof(TakeExtraTurnActionDefinition), "TAKE_EXTRA_TURN")]
public abstract record ActionDefinition(string ActionId);

public sealed record DealDamageActionDefinition(
  string ActionId,
  ValueExpression Amount,
  TargetRef Target,
  string? DamageSchool,
  bool Lifesteal,
  bool Preventable) : ActionDefinition(ActionId);

public sealed record DamageDistributionActionDefinition(
  string ActionId,
  ValueExpression TotalAmount,
  EntityQuery CandidateTargets,
  string DistributionMode,
  string PerTargetInstancePolicy,
  string LifestealAggregationPolicy,
  string OverkillPolicy,
  bool FizzleIfNoTargets,
  string RngPolicy) : ActionDefinition(ActionId);

public sealed record HealActionDefinition(
  string ActionId,
  ValueExpression Amount,
  TargetRef Target) : ActionDefinition(ActionId);

public sealed record FullyHealActionDefinition(
  string ActionId,
  TargetRef Target) : ActionDefinition(ActionId);

public sealed record DrawCardActionDefinition(
  string ActionId,
  PlayerSelector Player,
  ValueExpression Count,
  string DeckEmptyPolicy,
  string? ResultRef) : ActionDefinition(ActionId);

public sealed record DiscardCardActionDefinition(
  string ActionId,
  TargetRef Target,
  SourceZone FromZone,
  string InvalidTargetPolicy) : ActionDefinition(ActionId);

public sealed record SummonActionDefinition(
  string ActionId,
  SummonSource SummonSource,
  PlayerSelector Controller,
  string PlacementPolicy,
  ValueExpression Count,
  string BoardFullPolicy,
  string? ResultRef) : ActionDefinition(ActionId);

public sealed record AddCardToHandActionDefinition(
  string ActionId,
  CardSource CardSource,
  PlayerSelector Player,
  string HandFullPolicy,
  string? ResultRef) : ActionDefinition(ActionId);

public sealed record DestroyActionDefinition(
  string ActionId,
  TargetRef Target,
  string InvalidTargetPolicy) : ActionDefinition(ActionId);

public sealed record BatchDestroyActionDefinition(
  string ActionId,
  TargetRef Targets,
  string DeathProcessingPolicy,
  string InvalidTargetPolicy) : ActionDefinition(ActionId);

public sealed record RandomSelectActionDefinition(
  string ActionId,
  EntityQuery From,
  CountSpec Count,
  string ResultRef,
  string RngPolicy,
  string EmptyPolicy) : ActionDefinition(ActionId);

public sealed record MoveToZoneActionDefinition(
  string ActionId,
  TargetRef Target,
  SourceZone DestinationZone,
  string OwnershipPolicy,
  string VisibilityPolicy,
  string? ResultRef) : ActionDefinition(ActionId);

public sealed record ReturnToHandActionDefinition(
  string ActionId,
  TargetRef Target,
  string Owner,
  string HandFullPolicy,
  string? ResultRef) : ActionDefinition(ActionId);

public sealed record ApplyModifierActionDefinition(
  string ActionId,
  PlayerSelector Player,
  ModifierDefinition Modifier) : ActionDefinition(ActionId);

public sealed record ApplyAuraActionDefinition(
  string ActionId,
  PlayerSelector Controller,
  AuraDefinition Aura) : ActionDefinition(ActionId);

public sealed record PreventDamageActionDefinition(
  string ActionId,
  TargetRef Target,
  ValueExpression Amount,
  string Duration,
  string PreventionPolicy,
  string? ResultRef) : ActionDefinition(ActionId);

public sealed record ReplaceEventActionDefinition(
  string ActionId,
  string EventType,
  TargetRef Target,
  string ReplacementPolicy,
  string Duration,
  string? ResultRef) : ActionDefinition(ActionId);

public sealed record ShuffleIntoDeckActionDefinition(
  string ActionId,
  TargetRef Target,
  string ShufflePolicy,
  string? ResultRef) : ActionDefinition(ActionId);

public sealed record DiscoverActionDefinition(
  string ActionId,
  PlayerSelector Player,
  PoolRef PoolRef,
  int OfferCount,
  string ChoicePolicy,
  SourceZone AddToZone,
  string PoolEmptyPolicy,
  string? ResultRef) : ActionDefinition(ActionId);

public sealed record ResurrectActionDefinition(
  string ActionId,
  TargetRef Target,
  PlayerSelector Controller,
  ValueExpression Attack,
  ValueExpression Health,
  string PlacementPolicy,
  string BoardFullPolicy,
  string? ResultRef,
  HistoryQuerySpec? HistoryQuery = null) : ActionDefinition(ActionId);

public sealed record TakeExtraTurnActionDefinition(
  string ActionId,
  PlayerSelector Player,
  bool OncePerGame,
  bool CurrentTurnEndsNormally,
  string ExtraTurnStartPolicy,
  string TurnSchedulerPolicy,
  string? CommandButtonReplacement,
  string ModifierDurationInteractionPolicy) : ActionDefinition(ActionId);

public sealed record ValueExpression(string Kind, int? FixedValue, string? Ref);

public sealed record TargetRef(string Ref);

public sealed record SummonSource(string Kind, string Ref);

public sealed record CardSource(string Kind, string Ref);

public sealed record PoolRef(string Ref);

public sealed record HistoryQuerySpec(
  string Kind,
  PlayerSelector Player,
  string Ordering,
  int MaxCandidates);

public sealed record ModifierDefinition(
  string? ModifierId,
  string ModifierType,
  ValueExpression Value,
  string Duration,
  int Priority);

public sealed record AuraDefinition(
  string? AuraId,
  string AuraType,
  EntityQuery AffectedEntities,
  ModifierDefinition Modifier,
  string Duration,
  int Priority);

[JsonConverter(typeof(JsonStringEnumConverter<PlayerSelector>))]
public enum PlayerSelector
{
  [JsonStringEnumMemberName("SELF")]
  Self,

  [JsonStringEnumMemberName("OPPONENT")]
  Opponent,

  [JsonStringEnumMemberName("SOURCE_CONTROLLER")]
  SourceController
}

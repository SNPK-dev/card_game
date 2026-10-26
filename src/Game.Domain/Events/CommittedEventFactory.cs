using Game.Domain.Mutation;
using Game.Domain.Replacements;
using Game.Shared.Events;
using Game.Shared.Primitives;

namespace Game.Domain.Events;

public sealed class CommittedEventFactory : ICommittedEventFactory
{
  public IReadOnlyList<CommittedEventRecord> CreateCommittedEvents(
    AppliedMutationBatch appliedBatch,
    long firstEventSeq)
  {
    ArgumentNullException.ThrowIfNull(appliedBatch);
    if (firstEventSeq < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(firstEventSeq), "First event sequence must be positive.");
    }

    var events = new List<CommittedEventRecord>(
      appliedBatch.ProposedEvents.Count
      + appliedBatch.ReplacementDecisions.Count
      + appliedBatch.AppliedMutations.Count);
    var nextEventSeq = firstEventSeq;

    var proposedById = appliedBatch.ProposedEvents
      .OrderBy(@event => @event.EventSeqCandidate)
      .ThenBy(@event => @event.EventId, StringComparer.Ordinal)
      .ToDictionary(@event => @event.EventId, StringComparer.Ordinal);
    foreach (var proposedEvent in proposedById.Values)
    {
      var eventSeq = Math.Max(nextEventSeq, proposedEvent.EventSeqCandidate);
      events.Add(CreateProposedEvent(appliedBatch, proposedEvent, eventSeq));
      nextEventSeq = eventSeq + 1;
    }

    foreach (var decision in appliedBatch.ReplacementDecisions
      .OrderBy(decision => decision.EventSeqCandidate)
      .ThenBy(decision => decision.DecisionId, StringComparer.Ordinal))
    {
      proposedById.TryGetValue(decision.ProposedEventId, out var proposedEvent);
      events.Add(CreateReplacementDecisionEvent(appliedBatch, decision, proposedEvent, nextEventSeq++));
    }

    foreach (var mutation in appliedBatch.AppliedMutations)
    {
      events.Add(CreateCommittedEvent(appliedBatch, mutation, nextEventSeq++));
    }

    return events;
  }

  private static CommittedEventRecord CreateProposedEvent(
    AppliedMutationBatch appliedBatch,
    ProposedEventRecord proposedEvent,
    long eventSeq) =>
    new(
      eventSeq,
      proposedEvent.ParentEventSeq,
      proposedEvent.CommandSeq,
      ToCommittedEventType(proposedEvent.ProposedEventType),
      EventCategory.Proposed,
      proposedEvent.Phase,
      proposedEvent.SourceRefs,
      proposedEvent.TargetRefs,
      CreateProposedPayload(proposedEvent),
      appliedBatch.BatchId,
      [proposedEvent.ProposedOperationRef],
      proposedEvent.RngRollRefs,
      appliedBatch.PreStateHash,
      appliedBatch.PreStateHash,
      proposedEvent.Visibility);

  private static CommittedEventRecord CreateReplacementDecisionEvent(
    AppliedMutationBatch appliedBatch,
    ReplacementDecision decision,
    ProposedEventRecord? proposedEvent,
    long eventSeq) =>
    new(
      eventSeq,
      proposedEvent?.EventSeqCandidate,
      appliedBatch.CommandSeq,
      CommittedEventType.ReplacementResolved,
      EventCategory.Resolver,
      proposedEvent?.Phase ?? ToPhase(appliedBatch.Phase),
      proposedEvent?.SourceRefs ?? [new EventRef("SOURCE", appliedBatch.SourceRef, null, null)],
      proposedEvent?.TargetRefs ?? [],
      CreateReplacementDecisionPayload(decision),
      appliedBatch.BatchId,
      proposedEvent is null ? [] : [proposedEvent.ProposedOperationRef],
      [],
      appliedBatch.PreStateHash,
      appliedBatch.PreStateHash,
      EventVisibility.DebugOnly);

  private static CommittedEventRecord CreateCommittedEvent(
    AppliedMutationBatch appliedBatch,
    StateMutation mutation,
    long eventSeq)
  {
    var eventType = ToCommittedEventType(mutation);
    var payload = CreatePayload(mutation);

    return new CommittedEventRecord(
      eventSeq,
      appliedBatch.ParentEventSeq,
      appliedBatch.CommandSeq,
      eventType,
      EventCategory.Committed,
      ToPhase(appliedBatch.Phase),
      [new EventRef("SOURCE", mutation.SourceRef, null, null)],
      CreateTargetRefs(mutation),
      payload,
      appliedBatch.BatchId,
      [mutation.MutationId],
      [],
      appliedBatch.PreStateHash,
      appliedBatch.PostStateHash,
      EventVisibility.Public);
  }

  private static CommittedEventType ToCommittedEventType(StateMutation mutation)
  {
    return mutation.MutationType switch
    {
      AddKeywordMutationHandler.SupportedMutationType => CommittedEventType.ModifierApplied,
      RemoveKeywordMutationHandler.SupportedMutationType => CommittedEventType.ModifierExpired,
      IncrementCounterMutationHandler.SupportedMutationType => CommittedEventType.ModifierApplied,
      ApplyModifierMutationHandler.SupportedMutationType => CommittedEventType.ModifierApplied,
      CreateCardInstanceMutationHandler.SupportedMutationType => CommittedEventType.CardMovedZone,
      CreateChoiceMutationHandler.SupportedMutationType => CommittedEventType.ChoiceCreated,
      CreateLocationMutationHandler.SupportedMutationType => CommittedEventType.LocationPlayed,
      DamageEquippedEntityMutationHandler.SupportedMutationType => CommittedEventType.EntityDamaged,
      DestroyEquippedEntityMutationHandler.SupportedMutationType => CommittedEventType.EntityDestroyed,
      ResolveChoiceMutationHandler.SupportedMutationType => CommittedEventType.ChoiceResolved,
      MoveCardZoneMutationHandler.SupportedMutationType => CommittedEventType.CardMovedZone,
      DrawCardMutationHandler.SupportedMutationType => IsBurnedDraw(mutation)
        ? CommittedEventType.CardDiscarded
        : CommittedEventType.CardDrawn,
      EquipWeaponMutationHandler.SupportedMutationType => CommittedEventType.WeaponEquipped,
      PlayHeroCardMutationHandler.SupportedMutationType => CommittedEventType.HeroCardPlayed,
      ReplaceHeroPowerMutationHandler.SupportedMutationType => CommittedEventType.HeroPowerReplaced,
      SummonEntityMutationHandler.SupportedMutationType => CommittedEventType.MinionSummoned,
      ApplyDamageMutationHandler.SupportedMutationType => CommittedEventType.EntityDamaged,
      HealEntityMutationHandler.SupportedMutationType => CommittedEventType.EntityHealed,
      ModifyManaMutationHandler.SupportedMutationType => CommittedEventType.ManaChanged,
      ModifyFatigueMutationHandler.SupportedMutationType => CommittedEventType.ModifierApplied,
      ModifyAttackMutationHandler.SupportedMutationType => CommittedEventType.ModifierApplied,
      ModifyHealthMutationHandler.SupportedMutationType => CommittedEventType.ModifierApplied,
      AdvanceTurnMutationHandler.SupportedMutationType => CommittedEventType.TurnStarted,
      DestroyEntityMutationHandler.SupportedMutationType => CommittedEventType.EntityDestroyed,
      ReturnEntityToHandMutationHandler.SupportedMutationType => CommittedEventType.CardReturnedToHand,
      ScheduleExtraTurnMutationHandler.SupportedMutationType => CommittedEventType.ExtraTurnScheduled,
      RemoveModifierMutationHandler.SupportedMutationType => CommittedEventType.ModifierExpired,
      RefreshAurasMutationHandler.SupportedMutationType => CommittedEventType.AuraRefreshed,
      RevealSecretMutationHandler.SupportedMutationType => CommittedEventType.SecretRevealed,
      SetTerminalStateMutationHandler.SupportedMutationType => IsEngineFault(mutation)
        ? CommittedEventType.EngineFaulted
        : CommittedEventType.GameEnded,
      SetSecretMutationHandler.SupportedMutationType => CommittedEventType.CardMovedZone,
      UseHeroPowerMutationHandler.SupportedMutationType => CommittedEventType.HeroPowerUsed,
      UseLocationMutationHandler.SupportedMutationType => CommittedEventType.LocationActivated,
      UpdateQuestProgressMutationHandler.SupportedMutationType => CommittedEventType.QuestProgressChanged,
      _ => throw new InvalidOperationException(
        $"Mutation type '{mutation.MutationType}' does not have a committed event mapping.")
    };
  }

  private static CommittedEventType ToCommittedEventType(ProposedEventType proposedEventType) =>
    proposedEventType switch
    {
      ProposedEventType.DamageWouldBeDealt => CommittedEventType.DamageWouldBeDealt,
      _ => throw new InvalidOperationException(
        $"Proposed event type '{proposedEventType}' does not have a committed event mapping.")
    };

  private static IReadOnlyList<EventRef> CreateTargetRefs(StateMutation mutation)
  {
    if (mutation.TargetRef is null)
    {
      return [];
    }

    var refType = mutation.MutationType switch
    {
      AddKeywordMutationHandler.SupportedMutationType => "ENTITY_INSTANCE",
      RemoveKeywordMutationHandler.SupportedMutationType => "ENTITY_INSTANCE",
      MoveCardZoneMutationHandler.SupportedMutationType => "CARD_INSTANCE",
      CreateCardInstanceMutationHandler.SupportedMutationType => "CARD_INSTANCE",
      CreateLocationMutationHandler.SupportedMutationType => "ENTITY_INSTANCE",
      DamageEquippedEntityMutationHandler.SupportedMutationType => "ENTITY_INSTANCE",
      DestroyEquippedEntityMutationHandler.SupportedMutationType => "ENTITY_INSTANCE",
      DrawCardMutationHandler.SupportedMutationType => "CARD_INSTANCE",
      EquipWeaponMutationHandler.SupportedMutationType => "ENTITY_INSTANCE",
      PlayHeroCardMutationHandler.SupportedMutationType => "PLAYER",
      ReplaceHeroPowerMutationHandler.SupportedMutationType => "PLAYER",
      SummonEntityMutationHandler.SupportedMutationType => "ENTITY_INSTANCE",
      ApplyDamageMutationHandler.SupportedMutationType => "ENTITY_INSTANCE",
      HealEntityMutationHandler.SupportedMutationType => "ENTITY_INSTANCE",
      ApplyModifierMutationHandler.SupportedMutationType => "PLAYER",
      CreateChoiceMutationHandler.SupportedMutationType => "PLAYER",
      ResolveChoiceMutationHandler.SupportedMutationType => "PLAYER",
      ModifyManaMutationHandler.SupportedMutationType => "PLAYER",
      ModifyFatigueMutationHandler.SupportedMutationType => "PLAYER",
      ModifyAttackMutationHandler.SupportedMutationType => "ENTITY_INSTANCE",
      ModifyHealthMutationHandler.SupportedMutationType => "ENTITY_INSTANCE",
      AdvanceTurnMutationHandler.SupportedMutationType => "PLAYER",
      DestroyEntityMutationHandler.SupportedMutationType => "ENTITY_INSTANCE",
      ReturnEntityToHandMutationHandler.SupportedMutationType => "ENTITY_INSTANCE",
      ScheduleExtraTurnMutationHandler.SupportedMutationType => "PLAYER",
      RemoveModifierMutationHandler.SupportedMutationType => "PLAYER",
      RefreshAurasMutationHandler.SupportedMutationType => "PLAYER",
      RevealSecretMutationHandler.SupportedMutationType => "CARD_INSTANCE",
      SetTerminalStateMutationHandler.SupportedMutationType => "GAME",
      SetSecretMutationHandler.SupportedMutationType => "CARD_INSTANCE",
      UseHeroPowerMutationHandler.SupportedMutationType => "PLAYER",
      UseLocationMutationHandler.SupportedMutationType => "ENTITY_INSTANCE",
      UpdateQuestProgressMutationHandler.SupportedMutationType => "PLAYER",
      _ => "TARGET"
    };

    return [new EventRef(refType, mutation.TargetRef, null, null)];
  }

  private static bool IsEngineFault(StateMutation mutation) =>
    mutation.Metadata is not null
    && mutation.Metadata.TryGetValue("reason", out var reason)
    && StringComparer.Ordinal.Equals(reason, "ENGINE_FAULT");

  private static bool IsBurnedDraw(StateMutation mutation) =>
    mutation.Metadata is not null
    && mutation.Metadata.TryGetValue("destinationZone", out var destinationZone)
    && StringComparer.Ordinal.Equals(destinationZone, "GRAVEYARD");

  private static IReadOnlyList<EventPayloadEntry> CreatePayload(StateMutation mutation)
  {
    var payload = new List<EventPayloadEntry>();

    foreach (var entry in mutation.Payload.OrderBy(entry => entry.Key, StringComparer.Ordinal))
    {
      payload.Add(
        new EventPayloadEntry(
          entry.Key,
          new EventPayloadValue("INT", null, entry.Value, null, [])));
    }

    if (mutation.Metadata is not null)
    {
      foreach (var entry in mutation.Metadata.OrderBy(entry => entry.Key, StringComparer.Ordinal))
      {
        payload.Add(
          new EventPayloadEntry(
            entry.Key,
            new EventPayloadValue("STRING", entry.Value, null, null, [])));
      }
    }

    payload.Add(
      new EventPayloadEntry(
        "mutationType",
        new EventPayloadValue("STRING", mutation.MutationType, null, null, [])));

    return payload;
  }

  private static IReadOnlyList<EventPayloadEntry> CreateProposedPayload(ProposedEventRecord proposedEvent)
  {
    var payload = proposedEvent.Payload
      .OrderBy(entry => entry.Key, StringComparer.Ordinal)
      .ToList();

    payload.Add(new EventPayloadEntry(
      "preventionAllowed",
      new EventPayloadValue("BOOL", null, null, proposedEvent.PreventionAllowed, [])));
    payload.Add(new EventPayloadEntry(
      "proposedEventId",
      new EventPayloadValue("STRING", proposedEvent.EventId, null, null, [])));
    payload.Add(new EventPayloadEntry(
      "proposedEventType",
      new EventPayloadValue("STRING", proposedEvent.ProposedEventType.ToString(), null, null, [])));
    payload.Add(new EventPayloadEntry(
      "proposedOperationRef",
      new EventPayloadValue("STRING", proposedEvent.ProposedOperationRef, null, null, [])));
    payload.Add(new EventPayloadEntry(
      "replacementWindow",
      new EventPayloadValue("STRING", proposedEvent.ReplacementWindow, null, null, [])));

    return payload
      .OrderBy(entry => entry.Key, StringComparer.Ordinal)
      .ToArray();
  }

  private static IReadOnlyList<EventPayloadEntry> CreateReplacementDecisionPayload(ReplacementDecision decision) =>
  [
    new EventPayloadEntry(
      "decisionId",
      new EventPayloadValue("STRING", decision.DecisionId, null, null, [])),
    new EventPayloadEntry(
      "proposedEventId",
      new EventPayloadValue("STRING", decision.ProposedEventId, null, null, [])),
    new EventPayloadEntry(
      "proposedEventType",
      new EventPayloadValue("STRING", decision.ProposedEventType.ToString(), null, null, [])),
    new EventPayloadEntry(
      "reason",
      new EventPayloadValue("STRING", decision.Reason, null, null, [])),
    new EventPayloadEntry(
      "result",
      new EventPayloadValue("STRING", decision.Result.ToString(), null, null, []))
  ];

  private static Phase ToPhase(string phase)
  {
    return phase switch
    {
      "ON_PLAY" => Phase.OnPlay,
      "BATTLECRY_PHASE" => Phase.BattlecryPhase,
      "ON_SUMMON" => Phase.OnSummon,
      "AFTER_PLAY" => Phase.AfterPlay,
      "SECRET_WINDOW" => Phase.SecretWindow,
      "DEATHRATTLE_PHASE" => Phase.DeathrattlePhase,
      "TURN_START" => Phase.TurnStart,
      "TURN_END" => Phase.TurnEnd,
      "AURA_REFRESH" => Phase.AuraRefresh,
      "QUEST_PROGRESS" => Phase.AfterPlay,
      "TEST" => Phase.OnPlay,
      "COMMAND" => Phase.OnPlay,
      _ => throw new InvalidOperationException($"Phase '{phase}' does not have a committed event mapping.")
    };
  }
}

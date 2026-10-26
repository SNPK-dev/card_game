using System.Globalization;
using Game.Domain.Events;
using Game.Domain.Mutation;
using Game.Domain.Replacements;
using Game.Domain.State;
using Game.Shared.Events;
using Game.Shared.Primitives;

namespace Game.Application.Execution;

public sealed class DeterministicMutationBatchExecutor(
  IStateMutator mutator,
  ICommittedEventFactory eventFactory,
  IReplacementResolver? replacementResolver = null,
  EventQueueOptions? eventQueueOptions = null)
{
  private readonly IReplacementResolver _replacementResolver = replacementResolver ?? new ReplacementResolver();

  public MutationBatchExecutionResult Execute(
    IGameState state,
    ProposedMutationBatch batch,
    long firstEventSeq = 1)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(batch);
    if (firstEventSeq < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(firstEventSeq), "First event sequence must be positive.");
    }

    var replacementWindow = ResolveReplacementWindow(state, batch, firstEventSeq);
    var application = mutator.Apply(state, replacementWindow.FinalBatch);
    var appliedBatch = application.AppliedBatch with
    {
      ProposedEvents = replacementWindow.ProposedEvents,
      ReplacementDecisions = replacementWindow.Decisions
    };
    var committedEvents = eventFactory.CreateCommittedEvents(appliedBatch, firstEventSeq);
    var eventQueue = new EventQueue(eventQueueOptions, (int)firstEventSeq);

    foreach (var committedEvent in committedEvents)
    {
      eventQueue.EnqueueRoot(ToQueuedEvent(committedEvent));
    }

    var processedEvents = eventQueue.Drain();

    return new MutationBatchExecutionResult(
      application.State,
      appliedBatch,
      committedEvents,
      processedEvents);
  }

  private ReplacementWindowResult ResolveReplacementWindow(
    IGameState state,
    ProposedMutationBatch batch,
    long firstEventSeq)
  {
    var proposedEvents = CreateProposedEvents(state, batch, firstEventSeq);
    if (proposedEvents.Count == 0)
    {
      return new ReplacementWindowResult(batch, [], []);
    }

    var result = _replacementResolver.Resolve(new ReplacementResolutionRequest(
      state,
      batch,
      proposedEvents));
    if (!result.IsAccepted || result.FinalBatch is null)
    {
      var diagnostic = result.Diagnostics.FirstOrDefault();
      throw new InvalidOperationException(
        diagnostic is null
          ? "Replacement resolution rejected the proposed mutation batch."
          : $"Replacement resolution rejected the proposed mutation batch: {diagnostic.Code}.");
    }

    return new ReplacementWindowResult(result.FinalBatch, proposedEvents, result.Decisions);
  }

  private static IReadOnlyList<ProposedEventRecord> CreateProposedEvents(
    IGameState state,
    ProposedMutationBatch batch,
    long firstEventSeq)
  {
    var events = new List<ProposedEventRecord>();
    foreach (var mutation in batch.Mutations
      .OrderBy(mutation => mutation.OrderingKey, StringComparer.Ordinal)
      .ThenBy(mutation => mutation.MutationId, StringComparer.Ordinal))
    {
      if (!StringComparer.Ordinal.Equals(mutation.MutationType, ApplyDamageMutationHandler.SupportedMutationType))
      {
        continue;
      }

      if (!mutation.Payload.TryGetValue("amount", out var amount) || amount <= 0)
      {
        continue;
      }

      var preventionAllowed = mutation.Metadata is null
        || !mutation.Metadata.TryGetValue("preventable", out var preventable)
        || bool.TryParse(preventable, out var parsed) && parsed;
      if (!preventionAllowed || !HasReplacementCandidate(state, mutation))
      {
        continue;
      }

      events.Add(new ProposedEventRecord(
        $"proposed:{mutation.MutationId}:damage",
        firstEventSeq + events.Count,
        batch.CommandSeq,
        batch.ParentEventSeq,
        ToPhase(batch.Phase),
        ProposedEventType.DamageWouldBeDealt,
        [new EventRef("SOURCE", mutation.SourceRef, null, null)],
        mutation.TargetRef is null ? [] : [new EventRef("ENTITY_INSTANCE", mutation.TargetRef, null, mutation.Metadata?.GetValueOrDefault("playerId"))],
        mutation.MutationId,
        [
          new EventPayloadEntry(
            "amount",
            new EventPayloadValue("INT", null, amount, null, []))
        ],
        batch.ReplacementPolicyRef ?? "replacement:damage:mvp",
        preventionAllowed,
        [],
        EventVisibility.DebugOnly));
    }

    return events;
  }

  private static bool HasReplacementCandidate(IGameState state, StateMutation mutation)
  {
    if (mutation.Metadata is null
      || !mutation.Metadata.TryGetValue("playerId", out var playerId)
      || !state.Players.TryGetValue(playerId, out var player))
    {
      return false;
    }

    var hasPrevention = player.Modifiers.Any(modifier =>
      StringComparer.Ordinal.Equals(modifier.ModifierType, PlayerModifierTypes.DamagePrevention)
      && StringComparer.Ordinal.Equals(modifier.Duration, PlayerModifierDurations.NextDamage));
    if (hasPrevention)
    {
      return true;
    }

    return mutation.Metadata.TryGetValue("entityInstanceId", out var entityInstanceId)
      && player.Zones.Board.Any(entity =>
        StringComparer.Ordinal.Equals(entity.EntityInstanceId, entityInstanceId)
        && (entity.Keywords ?? []).Contains("DIVINE_SHIELD", StringComparer.Ordinal));
  }

  private static Phase ToPhase(string phase) =>
    phase switch
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
      _ => Phase.OnPlay
    };

  private static QueuedEvent ToQueuedEvent(CommittedEventRecord committedEvent)
  {
    return new QueuedEvent(
      $"committed:{committedEvent.EventSeq}",
      committedEvent.Category,
      committedEvent.EventType.ToString(),
      committedEvent.ParentEventSeq is null ? null : checked((int)committedEvent.ParentEventSeq.Value),
      committedEvent.CommandSeq is null ? 0 : checked((int)committedEvent.CommandSeq.Value),
      committedEvent.Phase.ToString(),
      committedEvent.SourceRefs.FirstOrDefault()?.RefId ?? "SYSTEM",
      committedEvent.TargetRefs.Select(target => target.RefId).ToArray(),
      [committedEvent.EventSeq.ToString("D20", CultureInfo.InvariantCulture)],
      committedEvent.ParentEventSeq is null ? 0 : 1,
      committedEvent.Visibility);
  }

  private sealed record ReplacementWindowResult(
    ProposedMutationBatch FinalBatch,
    IReadOnlyList<ProposedEventRecord> ProposedEvents,
    IReadOnlyList<ReplacementDecision> Decisions);
}

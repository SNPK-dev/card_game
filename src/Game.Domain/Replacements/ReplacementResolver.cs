using Game.Shared.Events;
using Game.Domain.Mutation;
using Game.Domain.State;

namespace Game.Domain.Replacements;

public sealed class ReplacementResolver : IReplacementResolver
{
  private const string DivineShieldKeyword = "DIVINE_SHIELD";

  public ReplacementResolutionResult Resolve(ReplacementResolutionRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.State);
    ArgumentNullException.ThrowIfNull(request.ProposedBatch);
    ArgumentNullException.ThrowIfNull(request.ProposedEvents);

    var policy = request.Policy ?? ReplacementResolutionPolicy.MvpDefaults;
    if (policy.MaxProposedEvents < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(request), "Max proposed events cannot be negative.");
    }

    var diagnostics = Validate(request.ProposedEvents, policy);
    if (diagnostics.Count > 0)
    {
      return ReplacementResolutionResult.Rejected(diagnostics);
    }

    var mutations = request.ProposedBatch.Mutations.ToList();
    var decisions = new List<ReplacementDecision>();
    foreach (var proposedEvent in request.ProposedEvents
      .OrderBy(@event => @event.EventSeqCandidate)
      .ThenBy(@event => @event.EventId, StringComparer.Ordinal))
    {
      var resolution = ResolveProposedEvent(request.State, proposedEvent, mutations);
      if (resolution.Diagnostic is not null)
      {
        diagnostics.Add(resolution.Diagnostic);
        continue;
      }

      mutations = resolution.Mutations.ToList();
      decisions.Add(new ReplacementDecision(
        $"replacement-decision:{proposedEvent.EventSeqCandidate:0000000000}:{proposedEvent.EventId}",
        proposedEvent.EventId,
        proposedEvent.EventSeqCandidate,
        proposedEvent.ProposedEventType,
        resolution.Result,
        resolution.Reason));
    }

    if (diagnostics.Count > 0)
    {
      return ReplacementResolutionResult.Rejected(diagnostics);
    }

    return ReplacementResolutionResult.Accepted(
      request.ProposedBatch with { Mutations = mutations },
      decisions);
  }

  private static List<ReplacementResolutionDiagnostic> Validate(
    IReadOnlyList<ProposedEventRecord> proposedEvents,
    ReplacementResolutionPolicy policy)
  {
    var diagnostics = new List<ReplacementResolutionDiagnostic>();

    if (proposedEvents.Count > policy.MaxProposedEvents)
    {
      diagnostics.Add(new ReplacementResolutionDiagnostic(
        ReplacementResolutionDiagnosticCode.ProposedEventLimitExceeded,
        $"Proposed event count '{proposedEvents.Count}' exceeds limit '{policy.MaxProposedEvents}'."));
    }

    foreach (var proposedEvent in proposedEvents)
    {
      if (proposedEvent.EventSeqCandidate < 1)
      {
        diagnostics.Add(new ReplacementResolutionDiagnostic(
          ReplacementResolutionDiagnosticCode.InvalidEventSequenceCandidate,
          "Proposed event sequence candidate must be positive.",
          proposedEvent.EventId));
      }
    }

    return diagnostics;
  }

  private static ProposedEventResolution ResolveProposedEvent(
    IGameState state,
    ProposedEventRecord proposedEvent,
    IReadOnlyList<StateMutation> mutations)
  {
    if (proposedEvent.ProposedEventType is not ProposedEventType.DamageWouldBeDealt)
    {
      return ProposedEventResolution.Unchanged(mutations, "NO_REPLACEMENT_FOR_PROPOSED_EVENT_TYPE");
    }

    var mutationIndex = mutations
      .Select((mutation, index) => new { mutation, index })
      .SingleOrDefault(item => StringComparer.Ordinal.Equals(
        item.mutation.MutationId,
        proposedEvent.ProposedOperationRef));
    if (mutationIndex is null)
    {
      return ProposedEventResolution.Rejected(new ReplacementResolutionDiagnostic(
        ReplacementResolutionDiagnosticCode.MissingProposedMutation,
        $"Proposed mutation '{proposedEvent.ProposedOperationRef}' was not found.",
        proposedEvent.EventId));
    }

    var mutation = mutationIndex.mutation;
    if (!mutation.Payload.TryGetValue("amount", out var amount) || amount <= 0)
    {
      return ProposedEventResolution.Rejected(new ReplacementResolutionDiagnostic(
        ReplacementResolutionDiagnosticCode.InvalidDamagePayload,
        "Damage proposed event must reference a positive damage mutation amount.",
        proposedEvent.EventId));
    }

    if (!proposedEvent.PreventionAllowed
      || mutation.Metadata is null
      || !mutation.Metadata.TryGetValue("playerId", out var playerId)
      || !mutation.Metadata.TryGetValue("entityInstanceId", out var entityInstanceId)
      || !state.Players.TryGetValue(playerId, out var player))
    {
      return ProposedEventResolution.Unchanged(mutations, "NO_PREVENTION_AVAILABLE");
    }

    var shieldedEntity = player.Zones.Board.SingleOrDefault(entity =>
      StringComparer.Ordinal.Equals(entity.EntityInstanceId, entityInstanceId)
      && (entity.Keywords ?? []).Contains(DivineShieldKeyword, StringComparer.Ordinal));
    if (shieldedEntity is not null)
    {
      var shieldMutations = mutations.ToList();
      shieldMutations.RemoveAt(mutationIndex.index);
      shieldMutations.Add(new StateMutation(
        $"mutation:{mutation.CausedByCommandSeq}:consume-divine-shield:{entityInstanceId}:{proposedEvent.EventSeqCandidate:0000}",
        RemoveKeywordMutationHandler.SupportedMutationType,
        mutation.SourceRef,
        entityInstanceId,
        new SortedDictionary<string, int>(StringComparer.Ordinal),
        mutation.Phase,
        mutation.CausedByCommandSeq,
        mutation.CausedByEventSeq,
        $"{mutation.OrderingKey}:consume-divine-shield:{entityInstanceId}",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["entityInstanceId"] = entityInstanceId,
          ["keyword"] = DivineShieldKeyword,
          ["playerId"] = playerId,
          ["proposedEventId"] = proposedEvent.EventId,
          ["replacementPolicy"] = "DIVINE_SHIELD"
        }));

      return new ProposedEventResolution(
        shieldMutations,
        ReplacementDecisionResult.Cancelled,
        "DIVINE_SHIELD_CONSUMED",
        null);
    }

    var prevention = player.Modifiers
      .Where(modifier =>
        StringComparer.Ordinal.Equals(modifier.ModifierType, PlayerModifierTypes.DamagePrevention)
        && StringComparer.Ordinal.Equals(modifier.Duration, PlayerModifierDurations.NextDamage))
      .OrderByDescending(modifier => modifier.Priority)
      .ThenBy(modifier => modifier.ModifierId, StringComparer.Ordinal)
      .FirstOrDefault();
    if (prevention is null)
    {
      return ProposedEventResolution.Unchanged(mutations, "NO_PREVENTION_AVAILABLE");
    }

    var preventedAmount = Math.Min(amount, prevention.Amount);
    var remainingAmount = amount - preventedAmount;
    var nextMutations = mutations.ToList();
    if (remainingAmount > 0)
    {
      nextMutations[mutationIndex.index] = mutation with
      {
        Payload = new SortedDictionary<string, int>(mutation.Payload.ToDictionary(
          entry => entry.Key,
          entry => entry.Value,
          StringComparer.Ordinal), StringComparer.Ordinal)
        {
          ["amount"] = remainingAmount
        },
        Metadata = new SortedDictionary<string, string>((mutation.Metadata ?? new Dictionary<string, string>())
          .ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal), StringComparer.Ordinal)
        {
          ["preventedAmount"] = preventedAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
          ["preventionModifierId"] = prevention.ModifierId
        }
      };
    }
    else
    {
      nextMutations.RemoveAt(mutationIndex.index);
    }

    nextMutations.Add(new StateMutation(
      $"mutation:{mutation.CausedByCommandSeq}:consume-prevention:{prevention.ModifierId}:{proposedEvent.EventSeqCandidate:0000}",
      RemoveModifierMutationHandler.SupportedMutationType,
      prevention.SourceRef,
      playerId,
      new SortedDictionary<string, int>(StringComparer.Ordinal),
      mutation.Phase,
      mutation.CausedByCommandSeq,
      mutation.CausedByEventSeq,
      $"{mutation.OrderingKey}:consume-prevention:{prevention.ModifierId}",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["modifierId"] = prevention.ModifierId,
        ["playerId"] = playerId,
        ["proposedEventId"] = proposedEvent.EventId
      }));

    return new ProposedEventResolution(
      nextMutations,
      remainingAmount > 0 ? ReplacementDecisionResult.Modified : ReplacementDecisionResult.Cancelled,
      $"DAMAGE_PREVENTED:{preventedAmount}",
      null);
  }

  private sealed record ProposedEventResolution(
    IReadOnlyList<StateMutation> Mutations,
    ReplacementDecisionResult Result,
    string Reason,
    ReplacementResolutionDiagnostic? Diagnostic)
  {
    public static ProposedEventResolution Unchanged(
      IReadOnlyList<StateMutation> mutations,
      string reason) =>
      new(mutations, ReplacementDecisionResult.Unchanged, reason, null);

    public static ProposedEventResolution Rejected(ReplacementResolutionDiagnostic diagnostic) =>
      new([], ReplacementDecisionResult.Unchanged, string.Empty, diagnostic);
  }
}

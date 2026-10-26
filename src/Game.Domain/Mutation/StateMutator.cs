using Game.Domain.Commands;
using Game.Domain.Hashing;
using Game.Domain.Serialization;
using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class StateMutator : IStateMutator
{
  private readonly StateMutationHandlerRegistry _handlers;
  private readonly IStateHasher _stateHasher;

  public StateMutator()
    : this(
      new StateMutationHandlerRegistry(
      [
        new AddKeywordMutationHandler(),
        new RemoveKeywordMutationHandler(),
        new IncrementCounterMutationHandler(),
        new MoveCardZoneMutationHandler(),
        new DrawCardMutationHandler(),
        new EquipWeaponMutationHandler(),
        new SummonEntityMutationHandler(),
        new ApplyDamageMutationHandler(),
        new ApplyModifierMutationHandler(),
        new CreateCardInstanceMutationHandler(),
        new CreateChoiceMutationHandler(),
        new CreateLocationMutationHandler(),
        new DamageEquippedEntityMutationHandler(),
        new DestroyEquippedEntityMutationHandler(),
        new ResolveChoiceMutationHandler(),
        new HealEntityMutationHandler(),
        new ModifyManaMutationHandler(),
        new ModifyFatigueMutationHandler(),
        new ModifyAttackMutationHandler(),
        new ModifyHealthMutationHandler(),
        new PlayHeroCardMutationHandler(),
        new ReplaceHeroPowerMutationHandler(),
        new AdvanceTurnMutationHandler(),
        new DestroyEntityMutationHandler(),
        new ReturnEntityToHandMutationHandler(),
        new ScheduleExtraTurnMutationHandler(),
        new RemoveModifierMutationHandler(),
        new RefreshAurasMutationHandler(),
        new RevealSecretMutationHandler(),
        new SetTerminalStateMutationHandler(),
        new SetSecretMutationHandler(),
        new UseHeroPowerMutationHandler(),
        new UseLocationMutationHandler(),
        new UpdateQuestProgressMutationHandler()
      ]),
      new StateHasher(new CanonicalJsonSerializer()))
  {
  }

  public StateMutator(StateMutationHandlerRegistry handlers, IStateHasher stateHasher)
  {
    _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
    _stateHasher = stateHasher ?? throw new ArgumentNullException(nameof(stateHasher));
  }

  public IGameState Apply(IGameState state, IGameCommand command)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(command);

    var counterKey = $"command:{command.Kind}";
    var commandSeq = state.Revision + 1;
    var batch = new ProposedMutationBatch(
      $"batch:{commandSeq}:{command.Kind}",
      commandSeq,
      null,
      "COMMAND",
      $"command:{command.Kind}",
      "COMMAND_ADAPTER",
      [
        new StateMutation(
          $"mutation:{commandSeq}:{command.Kind}:increment-counter",
          IncrementCounterMutationHandler.SupportedMutationType,
          $"command:{command.Kind}",
          counterKey,
          new SortedDictionary<string, int>(StringComparer.Ordinal)
          {
            ["amount"] = 1
          },
          "COMMAND",
          commandSeq,
          null,
          $"command:{command.Kind}:0001")
      ],
      _stateHasher.HashGameplayState(state),
      null,
      null);

    return Apply(state, batch).State;
  }

  public StateMutationApplication Apply(IGameState state, ProposedMutationBatch batch)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(batch);

    var actualPreStateHash = _stateHasher.HashGameplayState(state);
    if (!StringComparer.Ordinal.Equals(batch.PreStateHash, actualPreStateHash))
    {
      throw new InvalidOperationException(
        $"Mutation batch '{batch.BatchId}' pre-state hash does not match current state.");
    }

    var nextState = state;
    var appliedMutations = new List<StateMutation>();

    foreach (var mutation in Canonicalize(batch.Mutations))
    {
      var handler = _handlers.GetRequiredHandler(mutation.MutationType);
      if (!handler.CanApply(nextState, mutation))
      {
        if (ShouldSkipMissingDamageTarget(mutation))
        {
          continue;
        }

        throw new InvalidOperationException(
          $"Cannot apply mutation '{mutation.MutationId}' of type '{mutation.MutationType}'.");
      }

      nextState = handler.Apply(nextState, mutation);
      appliedMutations.Add(mutation);
    }

    var appliedBatch = new AppliedMutationBatch(
      batch.BatchId,
      batch.CommandSeq,
      batch.ParentEventSeq,
      batch.Phase,
      batch.SourceRef,
      batch.Reason,
      appliedMutations,
      [],
      batch.PreStateHash,
      _stateHasher.HashGameplayState(nextState),
      [],
      nextState.Revision);

    return new StateMutationApplication(nextState, appliedBatch);
  }

  private static IEnumerable<StateMutation> Canonicalize(IEnumerable<StateMutation> mutations)
  {
    return mutations
      .OrderBy(mutation => mutation.OrderingKey, StringComparer.Ordinal)
      .ThenBy(mutation => mutation.MutationType, StringComparer.Ordinal)
      .ThenBy(mutation => mutation.SourceRef, StringComparer.Ordinal)
      .ThenBy(mutation => mutation.TargetRef ?? string.Empty, StringComparer.Ordinal)
      .ThenBy(mutation => mutation.MutationId, StringComparer.Ordinal);
  }

  private static bool ShouldSkipMissingDamageTarget(StateMutation mutation) =>
    StringComparer.Ordinal.Equals(mutation.MutationType, ApplyDamageMutationHandler.SupportedMutationType)
    && mutation.Metadata is not null
    && mutation.Metadata.TryGetValue("targetDisappearancePolicy", out var policy)
    && StringComparer.Ordinal.Equals(policy, "SKIP_MISSING_TARGET");
}

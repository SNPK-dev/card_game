using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class CreateChoiceMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "CREATE_CHOICE";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadChoice(mutation, out var choice)
      && mutation.Payload.TryGetValue("optionCount", out var optionCount)
      && optionCount == choice.Options.Count
      && optionCount > 0
      && state.Players.ContainsKey(choice.PlayerId)
      && !state.PendingChoices.ContainsKey(choice.ChoiceId);
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadChoice(mutation, out var choice))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing create choice metadata.");
    }

    if (!CanApply(state, mutation))
    {
      throw new InvalidOperationException(
        $"Cannot apply mutation '{mutation.MutationId}' of type '{mutation.MutationType}'.");
    }

    var mutableState = state as InMemoryGameState
      ?? new InMemoryGameState(
        state.Revision,
        state.Counters,
        state.Players,
        state.PresentationMetadata,
        state.ActivePlayerId,
        state.TurnNumber,
        state.Phase,
        state.TerminalState,
        state.PendingChoices);

    return mutableState.WithChoiceCreated(choice);
  }

  private static bool TryReadChoice(StateMutation mutation, out PendingChoice choice)
  {
    choice = default!;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("choiceId", out var choiceId)
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("choiceType", out var choiceType)
      || !values.TryGetValue("visibilityPolicy", out var visibilityPolicy)
      || !values.TryGetValue("resultZone", out var resultZone)
      || string.IsNullOrWhiteSpace(choiceId)
      || string.IsNullOrWhiteSpace(playerId)
      || string.IsNullOrWhiteSpace(choiceType)
      || string.IsNullOrWhiteSpace(visibilityPolicy)
      || string.IsNullOrWhiteSpace(resultZone))
    {
      return false;
    }

    if (!mutation.Payload.TryGetValue("optionCount", out var optionCount)
      || optionCount <= 0)
    {
      return false;
    }

    var options = new List<ChoiceOption>(optionCount);
    for (var index = 1; index <= optionCount; index++)
    {
      var prefix = $"option:{index:0000}:";
      if (!values.TryGetValue(prefix + "id", out var optionId)
        || !values.TryGetValue(prefix + "itemKind", out var itemKind)
        || !values.TryGetValue(prefix + "itemId", out var itemId)
        || string.IsNullOrWhiteSpace(optionId)
        || string.IsNullOrWhiteSpace(itemKind)
        || string.IsNullOrWhiteSpace(itemId))
      {
        return false;
      }

      options.Add(new ChoiceOption(optionId, itemKind, itemId));
    }

    if (options.Select(option => option.OptionId).Distinct(StringComparer.Ordinal).Count() != options.Count)
    {
      return false;
    }

    choice = new PendingChoice(
      choiceId,
      playerId,
      choiceType,
      visibilityPolicy,
      resultZone,
      mutation.SourceRef,
      options);
    return true;
  }
}

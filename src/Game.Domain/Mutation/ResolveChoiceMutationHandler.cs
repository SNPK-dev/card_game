using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class ResolveChoiceMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "RESOLVE_CHOICE";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && state.PendingChoices.TryGetValue(metadata.ChoiceId, out var choice)
      && StringComparer.Ordinal.Equals(choice.PlayerId, metadata.PlayerId)
      && choice.Options.Any(option => StringComparer.Ordinal.Equals(option.OptionId, metadata.SelectedOptionId));
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing resolve choice metadata.");
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

    return mutableState.WithChoiceResolved(metadata.ChoiceId, metadata.SelectedOptionId);
  }

  private static bool TryReadMetadata(StateMutation mutation, out ResolveChoiceMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("choiceId", out var choiceId)
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("selectedOptionId", out var selectedOptionId)
      || string.IsNullOrWhiteSpace(choiceId)
      || string.IsNullOrWhiteSpace(playerId)
      || string.IsNullOrWhiteSpace(selectedOptionId))
    {
      return false;
    }

    metadata = new ResolveChoiceMetadata(choiceId, playerId, selectedOptionId);
    return true;
  }

  private readonly record struct ResolveChoiceMetadata(
    string ChoiceId,
    string PlayerId,
    string SelectedOptionId);
}

using Game.Domain.State;
using Game.Shared.Primitives;

namespace Game.Domain.Mutation;

public sealed class RevealSecretMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "REVEAL_SECRET";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && (player.Secrets ?? []).Any(secret =>
        StringComparer.Ordinal.Equals(secret.SecretId, metadata.SecretId)
        && !secret.IsRevealed)
      && player.Zones.ContainsCardInZone(metadata.SourceCardInstanceId, SourceZone.SecretZone);
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata) || !CanApply(state, mutation))
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

    var revealed = mutableState.WithSecretRevealed(metadata.PlayerId, metadata.SecretId);
    return revealed.WithCardMoved(
      metadata.PlayerId,
      metadata.SourceCardInstanceId,
      SourceZone.SecretZone,
      SourceZone.Graveyard);
  }

  private static bool TryReadMetadata(StateMutation mutation, out SecretMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("secretId", out var secretId)
      || !values.TryGetValue("sourceCardInstanceId", out var sourceCardInstanceId))
    {
      return false;
    }

    metadata = new SecretMetadata(playerId, secretId, sourceCardInstanceId);
    return true;
  }

  private readonly record struct SecretMetadata(
    string PlayerId,
    string SecretId,
    string SourceCardInstanceId);
}

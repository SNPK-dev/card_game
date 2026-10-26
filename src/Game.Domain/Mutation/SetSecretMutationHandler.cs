using Game.Domain.State;
using Game.Shared.Primitives;

namespace Game.Domain.Mutation;

public sealed class SetSecretMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "SET_SECRET";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && player.Zones.ContainsCardInZone(metadata.SourceCardInstanceId, SourceZone.Graveyard)
      && !(player.Secrets ?? []).Any(secret => StringComparer.Ordinal.Equals(secret.SecretId, metadata.SecretId));
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

    var moved = mutableState.WithCardMoved(
      metadata.PlayerId,
      metadata.SourceCardInstanceId,
      SourceZone.Graveyard,
      SourceZone.SecretZone);

    return moved.WithSecretSet(
      metadata.PlayerId,
      new PlayerSecret(
        metadata.SecretId,
        metadata.SourceCardInstanceId,
        metadata.SourceCardVersion,
        metadata.PlayerId,
        metadata.RevealEventType,
        metadata.EffectId,
        IsRevealed: false));
  }

  private static bool TryReadMetadata(StateMutation mutation, out SecretMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("secretId", out var secretId)
      || !values.TryGetValue("sourceCardInstanceId", out var sourceCardInstanceId)
      || !values.TryGetValue("sourceCardVersion", out var sourceCardVersion)
      || !values.TryGetValue("revealEventType", out var revealEventType)
      || !values.TryGetValue("effectId", out var effectId))
    {
      return false;
    }

    metadata = new SecretMetadata(
      playerId,
      secretId,
      sourceCardInstanceId,
      sourceCardVersion,
      revealEventType,
      effectId);
    return true;
  }

  private readonly record struct SecretMetadata(
    string PlayerId,
    string SecretId,
    string SourceCardInstanceId,
    string SourceCardVersion,
    string RevealEventType,
    string EffectId);
}

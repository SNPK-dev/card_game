using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class RefreshAurasMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "REFRESH_AURAS";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && state.Players.ContainsKey(metadata.PlayerId);
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

    return mutableState.WithAurasRefreshed(metadata.PlayerId, metadata.Auras);
  }

  private static bool TryReadMetadata(StateMutation mutation, out AuraRefreshMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null || !values.TryGetValue("playerId", out var playerId))
    {
      return false;
    }

    var auras = new List<PlayerAura>();
    for (var index = 1; ; index++)
    {
      var prefix = $"aura:{index:0000}:";
      if (!values.TryGetValue(prefix + "auraId", out var auraId))
      {
        break;
      }

      if (!values.TryGetValue(prefix + "sourceEntityInstanceId", out var sourceEntityInstanceId)
        || !values.TryGetValue(prefix + "sourceCardInstanceId", out var sourceCardInstanceId)
        || !values.TryGetValue(prefix + "sourceCardVersion", out var sourceCardVersion)
        || !values.TryGetValue(prefix + "controllerPlayerId", out var controllerPlayerId)
        || !values.TryGetValue(prefix + "effectId", out var effectId)
        || !values.TryGetValue(prefix + "priority", out var priorityValue)
        || !int.TryParse(priorityValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var priority))
      {
        return false;
      }

      auras.Add(new PlayerAura(
        auraId,
        sourceEntityInstanceId,
        sourceCardInstanceId,
        sourceCardVersion,
        controllerPlayerId,
        effectId,
        priority));
    }

    metadata = new AuraRefreshMetadata(playerId, auras);
    return true;
  }

  private readonly record struct AuraRefreshMetadata(
    string PlayerId,
    IReadOnlyList<PlayerAura> Auras);
}

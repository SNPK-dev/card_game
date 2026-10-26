using Game.Domain.State;
using Game.Shared.Primitives;

namespace Game.Domain.Mutation;

public sealed class EquipWeaponMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "EQUIP_WEAPON";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && player.Weapon is null
      && player.Zones.ContainsCardInZone(metadata.SourceCardInstanceId, SourceZone.SetAside)
      && metadata.Attack >= 0
      && metadata.Durability > 0;
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing equip weapon metadata.");
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

    return mutableState.WithWeaponEquipped(
      metadata.PlayerId,
      new EntityInstance(
        metadata.EntityInstanceId,
        metadata.SourceCardInstanceId,
        metadata.PlayerId,
        EntityType.Weapon,
        metadata.Attack,
        metadata.Durability,
        Damage: 0,
        metadata.Keywords));
  }

  private static bool TryReadMetadata(StateMutation mutation, out EquipWeaponMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("entityInstanceId", out var entityInstanceId)
      || !values.TryGetValue("sourceCardInstanceId", out var sourceCardInstanceId)
      || !mutation.Payload.TryGetValue("attack", out var attack)
      || !mutation.Payload.TryGetValue("durability", out var durability))
    {
      return false;
    }

    metadata = new EquipWeaponMetadata(
      playerId,
      entityInstanceId,
      sourceCardInstanceId,
      attack,
      durability,
      ReadKeywords(values));
    return true;
  }

  private static IReadOnlyList<string> ReadKeywords(IReadOnlyDictionary<string, string> values) =>
    values
      .Where(pair => pair.Key.StartsWith("entityKeyword:", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(pair.Value))
      .OrderBy(pair => pair.Key, StringComparer.Ordinal)
      .Select(pair => pair.Value)
      .Distinct(StringComparer.Ordinal)
      .ToArray();

  private readonly record struct EquipWeaponMetadata(
    string PlayerId,
    string EntityInstanceId,
    string SourceCardInstanceId,
    int Attack,
    int Durability,
    IReadOnlyList<string> Keywords);
}

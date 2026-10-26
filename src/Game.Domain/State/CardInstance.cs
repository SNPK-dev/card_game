using Game.Shared.Primitives;

namespace Game.Domain.State;

public sealed record CardInstance(
  string CardInstanceId,
  string CardId,
  string CardVersion,
  string OwnerPlayerId,
  CardType CardType);

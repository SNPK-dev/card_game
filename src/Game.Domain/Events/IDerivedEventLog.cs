using Game.Domain.Commands;
using Game.Domain.State;

namespace Game.Domain.Events;

public interface IDerivedEventLog
{
  IReadOnlyList<DerivedEvent> Derive(IGameState before, IGameState after, IGameCommand command);
}

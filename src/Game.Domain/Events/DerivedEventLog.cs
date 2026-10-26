using Game.Domain.Commands;
using Game.Domain.State;

namespace Game.Domain.Events;

public sealed class DerivedEventLog : IDerivedEventLog
{
  public IReadOnlyList<DerivedEvent> Derive(IGameState before, IGameState after, IGameCommand command)
  {
    ArgumentNullException.ThrowIfNull(before);
    ArgumentNullException.ThrowIfNull(after);
    ArgumentNullException.ThrowIfNull(command);

    return
    [
      new DerivedEvent($"derived:{command.Kind}", before.Revision, after.Revision)
    ];
  }
}

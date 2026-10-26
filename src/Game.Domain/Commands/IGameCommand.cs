namespace Game.Domain.Commands;

public interface IGameCommand
{
  string Kind { get; }

  IReadOnlyDictionary<string, int> Parameters { get; }
}

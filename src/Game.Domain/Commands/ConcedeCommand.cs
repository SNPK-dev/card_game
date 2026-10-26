namespace Game.Domain.Commands;

public sealed record ConcedeCommand(string PlayerId) : IGameCommand
{
  public const string CommandKind = "CONCEDE";

  public string Kind => CommandKind;

  public IReadOnlyDictionary<string, int> Parameters { get; } =
    new SortedDictionary<string, int>(StringComparer.Ordinal);
}

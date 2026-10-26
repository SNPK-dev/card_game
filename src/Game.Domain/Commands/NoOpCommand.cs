namespace Game.Domain.Commands;

public sealed record NoOpCommand(string Kind = "noop", IReadOnlyDictionary<string, int>? Parameters = null)
  : IGameCommand
{
  IReadOnlyDictionary<string, int> IGameCommand.Parameters => Parameters ?? EmptyParameters.Value;

  private static class EmptyParameters
  {
    internal static readonly IReadOnlyDictionary<string, int> Value = new SortedDictionary<string, int>(StringComparer.Ordinal);
  }
}

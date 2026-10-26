using Game.Domain.Commands;

namespace Game.Application.Legality;

public sealed record LegalActionResult(
  IReadOnlyList<IGameCommand> Commands,
  IReadOnlyList<LegalActionDiagnostic> Diagnostics)
{
  public bool HasLegalActions => Commands.Count > 0;
}

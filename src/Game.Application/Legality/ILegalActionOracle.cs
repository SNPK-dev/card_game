namespace Game.Application.Legality;

public interface ILegalActionOracle
{
  LegalActionResult GetLegalActions(LegalActionQuery query);
}

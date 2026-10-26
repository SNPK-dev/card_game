namespace Game.Application.Matchmaking;

public interface IMatchReadinessService
{
  MatchReadinessResult Check(MatchReadinessRequest request);
}

namespace Game.Application.Matchmaking;

public interface IMatchStartService
{
  MatchStartResult Start(MatchStartRequest request);
}

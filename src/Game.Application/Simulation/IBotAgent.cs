namespace Game.Application.Simulation;

public interface IBotAgent
{
  BotDecision Decide(BotDecisionContext context);
}

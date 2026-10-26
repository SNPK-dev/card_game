using Game.Domain.State;

namespace Game.Domain.Conditions;

public interface IConditionEvaluator
{
  ConditionEvaluationResult Evaluate(IGameState state, ConditionEvaluationRequest request);
}

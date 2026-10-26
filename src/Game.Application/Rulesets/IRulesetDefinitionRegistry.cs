using Game.Shared.Ruleset;

namespace Game.Application.Rulesets;

public interface IRulesetDefinitionRegistry
{
  RulesetDefinition DefaultProductionRuleset { get; }

  IReadOnlyList<RulesetDefinition> GetAll();

  RulesetDefinition? Find(string rulesetId);

  RulesetDefinition? FindVersion(string rulesetId, string version);
}

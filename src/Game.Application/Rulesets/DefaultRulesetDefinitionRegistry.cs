using Game.Shared.Ruleset;

namespace Game.Application.Rulesets;

public sealed class DefaultRulesetDefinitionRegistry : IRulesetDefinitionRegistry
{
  public const string ProductionRulesetId = "ruleset-production-core-v1";
  public const string ProductionRulesetVersion = "1.0.0";

  private readonly IReadOnlyList<RulesetDefinition> rulesets =
  [
    new RulesetDefinition(
      ProductionRulesetId,
      ProductionRulesetVersion,
      "Production Core",
      RulesetEnvironment.Production,
      ProductionScale: true,
      new DeckRules(
        MinimumDeckSize: 30,
        MaximumDeckSize: 30,
        DefaultDeckSize: 30,
        MaximumCopiesPerCard: 2),
      new CardPoolPolicy(
        CatalogBound: true,
        RequirePublishedCardVersion: true,
        AllowUnknownCards: false)),
    new RulesetDefinition(
      "ruleset_mvp_core_v1",
      "0.1.0",
      "MVP Core",
      RulesetEnvironment.Development,
      ProductionScale: false,
      new DeckRules(
        MinimumDeckSize: 1,
        MaximumDeckSize: 100,
        DefaultDeckSize: 1,
        MaximumCopiesPerCard: 99),
      new CardPoolPolicy(
        CatalogBound: false,
        RequirePublishedCardVersion: false,
        AllowUnknownCards: false))
  ];

  public RulesetDefinition DefaultProductionRuleset => rulesets[0];

  public IReadOnlyList<RulesetDefinition> GetAll() => rulesets;

  public RulesetDefinition? Find(string rulesetId) =>
    rulesets.FirstOrDefault(ruleset =>
      string.Equals(ruleset.RulesetId, rulesetId, StringComparison.Ordinal));

  public RulesetDefinition? FindVersion(string rulesetId, string version) =>
    rulesets.FirstOrDefault(ruleset =>
      string.Equals(ruleset.RulesetId, rulesetId, StringComparison.Ordinal)
      && string.Equals(ruleset.RulesetVersion, version, StringComparison.Ordinal));
}

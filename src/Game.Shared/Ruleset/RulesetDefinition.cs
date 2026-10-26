namespace Game.Shared.Ruleset;

public static class RulesetEnvironment
{
  public const string Development = "development";
  public const string Test = "test";
  public const string Production = "production";
}

public sealed record RulesetDefinition(
  string RulesetId,
  string RulesetVersion,
  string DisplayName,
  string Environment,
  bool ProductionScale,
  DeckRules DeckRules,
  CardPoolPolicy CardPoolPolicy,
  IReadOnlyList<string>? CompatibleCatalogIds = null,
  IReadOnlyList<string>? CompatibleCatalogVersions = null);

public sealed record DeckRules(
  int MinimumDeckSize,
  int MaximumDeckSize,
  int DefaultDeckSize,
  int MaximumCopiesPerCard);

public sealed record CardPoolPolicy(
  bool CatalogBound,
  bool RequirePublishedCardVersion,
  bool AllowUnknownCards);

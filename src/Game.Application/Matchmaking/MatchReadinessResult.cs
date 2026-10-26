namespace Game.Application.Matchmaking;

public sealed record MatchReadinessResult(
  bool IsReady,
  IReadOnlyList<MatchReadinessDiagnostic> Diagnostics,
  string RulesetId = "",
  string RulesetVersion = "",
  string CatalogId = "",
  int ExpectedDeckSize = 0,
  int ActualDeckSize = 0,
  bool CopyLimitPassed = true,
  bool CardLegalityPassed = true)
{
  public static MatchReadinessResult Ready(
    string rulesetId = "",
    string rulesetVersion = "",
    string catalogId = "",
    int expectedDeckSize = 0,
    int actualDeckSize = 0) =>
    new(true, [], rulesetId, rulesetVersion, catalogId, expectedDeckSize, actualDeckSize);

  public static MatchReadinessResult NotReady(
    IReadOnlyList<MatchReadinessDiagnostic> diagnostics,
    string rulesetId = "",
    string rulesetVersion = "",
    string catalogId = "",
    int expectedDeckSize = 0,
    int actualDeckSize = 0,
    bool copyLimitPassed = true,
    bool cardLegalityPassed = true) =>
    new(false, diagnostics, rulesetId, rulesetVersion, catalogId, expectedDeckSize, actualDeckSize, copyLimitPassed, cardLegalityPassed);
}
